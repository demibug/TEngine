#!/usr/bin/env node
'use strict';

/**
 * Safe, repeatable string decoder for this protected LayaAir bundle.
 *
 * Safety model:
 * - Parses the complete input as JavaScript but never executes the game IIFE.
 * - Executes only the statically delimited pure runtime prefix (default lines 1-1000)
 *   in a Node vm context with dynamic code generation disabled.
 * - Does not provide wx, tt, Laya, require, process, network, filesystem, timers,
 *   Date, or a usable Math.random to the evaluated prefix.
 * - Preserves the anti-tamper range (default lines 1001-1014) byte-for-byte.
 * - Replaces only expressions proven pure and statically equal to a string.
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const vm = require('vm');
const childProcess = require('child_process');

function loadTypeScript() {
  try {
    return require('typescript');
  } catch (_) {
    try {
      const globalRoot = childProcess.execFileSync('npm', ['root', '-g'], {
        encoding: 'utf8',
        stdio: ['ignore', 'pipe', 'ignore'],
      }).trim();
      return require(path.join(globalRoot, 'typescript'));
    } catch (error) {
      throw new Error(
        'TypeScript parser is required. Run `npm install` in the project root.\n' +
        `Original loader error: ${error.message}`,
      );
    }
  }
}

const ts = loadTypeScript();

// Project-specific immutable baselines. The decoder refuses arbitrary JavaScript input.
const EXPECTED_BUNDLE_SHA256 = '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595';
const EXPECTED_RUNTIME_PREFIX_SHA256 = '180d9cc230e43ded3088726cd68f2fc661c64c7b8883036f2ddb6cfd41fde9e7';

function parseArgs(argv) {
  const result = {
    input: null,
    output: null,
    report: null,
    map: null,
    unresolved: null,
    runtimeValues: null,
    runtimeEvalEndLine: 1000,
    replacementStartLine: 1015,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (!arg.startsWith('--')) {
      throw new Error(`Unexpected argument: ${arg}`);
    }
    const key = arg.slice(2);
    const value = argv[++i];
    if (value == null) {
      throw new Error(`Missing value for --${key}`);
    }
    switch (key) {
      case 'input': result.input = value; break;
      case 'output': result.output = value; break;
      case 'report': result.report = value; break;
      case 'map': result.map = value; break;
      case 'unresolved': result.unresolved = value; break;
      case 'runtime-values': result.runtimeValues = value; break;
      case 'runtime-eval-end-line': result.runtimeEvalEndLine = Number(value); break;
      case 'replacement-start-line': result.replacementStartLine = Number(value); break;
      default: throw new Error(`Unknown option: --${key}`);
    }
  }

  for (const required of ['input', 'output', 'report', 'map', 'unresolved']) {
    if (!result[required]) {
      throw new Error(`Missing required option --${required.replace(/[A-Z]/g, m => '-' + m.toLowerCase())}`);
    }
  }
  if (!Number.isInteger(result.runtimeEvalEndLine) || result.runtimeEvalEndLine < 1) {
    throw new Error('--runtime-eval-end-line must be a positive integer');
  }
  if (!Number.isInteger(result.replacementStartLine) || result.replacementStartLine <= result.runtimeEvalEndLine) {
    throw new Error('--replacement-start-line must be greater than --runtime-eval-end-line');
  }
  return result;
}

function sha256(data) {
  return crypto.createHash('sha256').update(data).digest('hex');
}

function buildLineStarts(text) {
  const starts = [0];
  for (let i = 0; i < text.length; i += 1) {
    if (text.charCodeAt(i) === 10) starts.push(i + 1);
  }
  return starts;
}

function offsetAtLine(lineStarts, oneBasedLine, textLength) {
  if (oneBasedLine <= 1) return 0;
  const index = oneBasedLine - 1;
  return index < lineStarts.length ? lineStarts[index] : textLength;
}

function locationAt(lineStarts, offset) {
  let low = 0;
  let high = lineStarts.length - 1;
  while (low <= high) {
    const mid = (low + high) >>> 1;
    if (lineStarts[mid] <= offset) low = mid + 1;
    else high = mid - 1;
  }
  const lineIndex = Math.max(0, high);
  return { line: lineIndex + 1, column: offset - lineStarts[lineIndex] };
}

function cloneSerializable(value, depth = 0) {
  if (depth > 8) return undefined;
  if (value == null || ['string', 'number', 'boolean'].includes(typeof value)) return value;
  if (Array.isArray(value)) {
    return value.map(item => cloneSerializable(item, depth + 1));
  }
  return undefined;
}

function createSafeMath() {
  const safeMath = Object.create(null);
  for (const key of Object.getOwnPropertyNames(Math)) {
    if (key === 'random') continue;
    const descriptor = Object.getOwnPropertyDescriptor(Math, key);
    Object.defineProperty(safeMath, key, descriptor);
  }
  Object.defineProperty(safeMath, 'random', {
    value() {
      throw new Error('Math.random is disabled in the static runtime evaluator');
    },
    writable: false,
    configurable: false,
    enumerable: false,
  });
  return Object.freeze(safeMath);
}

function evaluateRuntimePrefix(prefixSource, filename) {
  const context = Object.create(null);
  context.Math = createSafeMath();
  for (const blockedName of [
    'wx', 'tt', 'Laya', 'require', 'process', 'fetch', 'XMLHttpRequest',
    'WebSocket', 'Date', 'performance', 'setTimeout', 'setInterval',
    'setImmediate', 'queueMicrotask', 'Function', 'eval', 'console',
  ]) {
    context[blockedName] = undefined;
  }
  vm.createContext(context, {
    name: 'bundle-string-runtime',
    codeGeneration: { strings: false, wasm: false },
  });

  vm.runInContext(prefixSource, context, {
    filename,
    timeout: 3000,
    displayErrors: true,
  });

  const captured = Object.create(null);
  for (const key of Object.keys(context)) {
    const cloned = cloneSerializable(context[key]);
    if (cloned !== undefined) captured[key] = cloned;
  }
  return captured;
}

function createProgram(fileName, sourceText) {
  const sourceFile = ts.createSourceFile(
    fileName,
    sourceText,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.JS,
  );
  const options = {
    allowJs: true,
    checkJs: false,
    noResolve: true,
    noLib: true,
    target: ts.ScriptTarget.Latest,
    module: ts.ModuleKind.None,
  };
  const host = {
    getSourceFile: requested => requested === fileName ? sourceFile : undefined,
    getDefaultLibFileName: () => '',
    writeFile: () => {},
    getCurrentDirectory: () => path.dirname(fileName),
    getDirectories: () => [],
    fileExists: requested => requested === fileName,
    readFile: requested => requested === fileName ? sourceText : undefined,
    getCanonicalFileName: name => name,
    useCaseSensitiveFileNames: () => true,
    getNewLine: () => '\n',
  };
  const program = ts.createProgram([fileName], options, host);
  return { program, sourceFile: program.getSourceFile(fileName) || sourceFile, checker: program.getTypeChecker() };
}

function own(obj, key) {
  return Object.prototype.hasOwnProperty.call(obj, key);
}

function known(value, sources = [], tags = []) {
  return { known: true, value, sources: new Set(sources), tags: new Set(tags) };
}

const UNKNOWN = Object.freeze({ known: false, value: undefined, sources: new Set(), tags: new Set() });

function unionSources(...values) {
  const result = new Set();
  for (const item of values) {
    if (!item || !item.sources) continue;
    for (const source of item.sources) result.add(source);
  }
  return result;
}

function hasStringRuntimeSource(value) {
  return value && value.known && (
    value.sources.has('string-table') ||
    value.sources.has('runtime-string')
  );
}

function isAssignmentOperator(kind) {
  return kind >= ts.SyntaxKind.FirstAssignment && kind <= ts.SyntaxKind.LastAssignment;
}

const resolvedSymbolCache = new WeakMap();
function getResolvedSymbol(checker, node) {
  if (resolvedSymbolCache.has(node)) return resolvedSymbolCache.get(node) || undefined;
  let symbol = checker.getSymbolAtLocation(node);
  if (symbol && (symbol.flags & ts.SymbolFlags.Alias)) {
    try { symbol = checker.getAliasedSymbol(symbol); } catch (_) { /* no-op */ }
  }
  resolvedSymbolCache.set(node, symbol || null);
  return symbol;
}

function markAssignmentTargetIdentifiers(tsApi, checker, node, targetMap, position) {
  function mark(target) {
    if (tsApi.isIdentifier(target)) {
      const symbol = getResolvedSymbol(checker, target);
      if (symbol) {
        const list = targetMap.get(symbol) || [];
        list.push(position);
        targetMap.set(symbol, list);
      }
      return;
    }
    if (tsApi.isArrayLiteralExpression(target) || tsApi.isArrayBindingPattern(target)) {
      for (const element of target.elements) {
        if (tsApi.isOmittedExpression(element)) continue;
        if (tsApi.isBindingElement(element)) mark(element.name);
        else if (tsApi.isSpreadElement(element)) mark(element.expression);
        else mark(element);
      }
      return;
    }
    if (tsApi.isObjectLiteralExpression(target) || tsApi.isObjectBindingPattern(target)) {
      for (const property of target.properties || target.elements) {
        if (tsApi.isBindingElement(property)) mark(property.name);
        else if (tsApi.isShorthandPropertyAssignment(property)) mark(property.name);
        else if (tsApi.isPropertyAssignment(property)) mark(property.initializer);
        else if (tsApi.isSpreadAssignment(property)) mark(property.expression);
      }
      return;
    }
    if (tsApi.isParenthesizedExpression(target)) mark(target.expression);
  }
  mark(node);
}

function collectWrites(sourceFile, checker) {
  const writes = new Map();
  function visit(node) {
    if (ts.isBinaryExpression(node) && isAssignmentOperator(node.operatorToken.kind)) {
      markAssignmentTargetIdentifiers(ts, checker, node.left, writes, node.left.getStart(sourceFile));
    } else if (
      (ts.isPrefixUnaryExpression(node) || ts.isPostfixUnaryExpression(node)) &&
      (node.operator === ts.SyntaxKind.PlusPlusToken || node.operator === ts.SyntaxKind.MinusMinusToken)
    ) {
      markAssignmentTargetIdentifiers(ts, checker, node.operand, writes, node.operand.getStart(sourceFile));
    } else if ((ts.isForInStatement(node) || ts.isForOfStatement(node)) && !ts.isVariableDeclarationList(node.initializer)) {
      markAssignmentTargetIdentifiers(ts, checker, node.initializer, writes, node.initializer.getStart(sourceFile));
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  return writes;
}

function collectVariableDeclarators(sourceFile, checker) {
  const result = [];
  function visit(node) {
    if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.initializer) {
      const symbol = getResolvedSymbol(checker, node.name);
      if (symbol) result.push({ node, symbol, name: node.name.text });
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  result.sort((a, b) => a.node.getStart(sourceFile) - b.node.getStart(sourceFile));
  return result;
}

function createStaticEvaluator(sourceFile, checker, bindings) {
  const pureStringMethods = new Set([
    'charAt', 'charCodeAt', 'slice', 'substring', 'substr', 'concat',
    'indexOf', 'lastIndexOf', 'startsWith', 'endsWith', 'includes',
    'toLowerCase', 'toUpperCase', 'trim', 'trimStart', 'trimEnd',
    'split', 'replace', 'replaceAll', 'toString',
  ]);
  const pureArrayMethods = new Set(['join', 'slice', 'concat', 'indexOf', 'lastIndexOf', 'includes']);

  function getBindingValue(identifier) {
    const symbol = getResolvedSymbol(checker, identifier);
    if (!symbol) return UNKNOWN;
    const binding = bindings.get(symbol);
    if (!binding || identifier.getStart(sourceFile) < binding.availableFrom) return UNKNOWN;
    return binding.staticValue;
  }

  const evaluationCache = new WeakMap();

  function evaluate(node, depth = 0) {
    if (!node || depth > 50) return UNKNOWN;
    if (evaluationCache.has(node)) return evaluationCache.get(node);
    const result = evaluateUncached(node, depth);
    evaluationCache.set(node, result);
    return result;
  }

  function evaluateUncached(node, depth = 0) {
    if (!node || depth > 50) return UNKNOWN;

    if (ts.isParenthesizedExpression(node)) return evaluate(node.expression, depth + 1);
    if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) return known(node.text, ['literal']);
    if (ts.isNumericLiteral(node)) return known(Number(node.text), ['literal']);
    if (node.kind === ts.SyntaxKind.TrueKeyword) return known(true, ['literal']);
    if (node.kind === ts.SyntaxKind.FalseKeyword) return known(false, ['literal']);
    if (node.kind === ts.SyntaxKind.NullKeyword) return known(null, ['literal']);

    if (ts.isIdentifier(node)) {
      if (node.text === 'undefined' && !getResolvedSymbol(checker, node)) return known(undefined, ['literal']);
      if (node.text === 'NaN' && !getResolvedSymbol(checker, node)) return known(NaN, ['literal']);
      if (node.text === 'Infinity' && !getResolvedSymbol(checker, node)) return known(Infinity, ['literal']);
      return getBindingValue(node);
    }

    if (ts.isArrayLiteralExpression(node)) {
      if (node.elements.length > 2000) return UNKNOWN;
      const values = [];
      const parts = [];
      for (const element of node.elements) {
        if (ts.isOmittedExpression(element) || ts.isSpreadElement(element)) return UNKNOWN;
        const item = evaluate(element, depth + 1);
        if (!item.known) return UNKNOWN;
        values.push(item.value);
        parts.push(item);
      }
      return known(values, unionSources(...parts));
    }

    if (ts.isObjectLiteralExpression(node)) {
      const object = Object.create(null);
      const parts = [];
      for (const property of node.properties) {
        if (!ts.isPropertyAssignment(property)) return UNKNOWN;
        let key;
        if (ts.isIdentifier(property.name) || ts.isStringLiteral(property.name) || ts.isNumericLiteral(property.name)) {
          key = property.name.text;
        } else if (ts.isComputedPropertyName(property.name)) {
          const computed = evaluate(property.name.expression, depth + 1);
          if (!computed.known) return UNKNOWN;
          key = String(computed.value);
          parts.push(computed);
        } else return UNKNOWN;
        const item = evaluate(property.initializer, depth + 1);
        if (!item.known) return UNKNOWN;
        object[key] = item.value;
        parts.push(item);
      }
      return known(object, unionSources(...parts));
    }

    if (ts.isElementAccessExpression(node)) {
      const object = evaluate(node.expression, depth + 1);
      const property = evaluate(node.argumentExpression, depth + 1);
      if (!object.known || !property.known) return UNKNOWN;
      const key = property.value;
      let value;
      if (typeof object.value === 'string') {
        if (typeof key !== 'number' && typeof key !== 'string') return UNKNOWN;
        value = object.value[key];
      } else if (Array.isArray(object.value)) {
        const index = typeof key === 'number' ? key : Number(key);
        if (!Number.isInteger(index) || index < 0 || index >= object.value.length) return UNKNOWN;
        value = object.value[index];
      } else if (object.value && typeof object.value === 'object') {
        const propertyKey = String(key);
        if (!own(object.value, propertyKey)) return UNKNOWN;
        value = object.value[propertyKey];
      } else return UNKNOWN;
      const tags = new Set();
      if (Array.isArray(value)) {
        if (object.tags && object.tags.has('string-table-root')) tags.add('string-table-array');
        else if (object.tags && object.tags.has('string-table-array')) tags.add('string-table-array');
        if (object.tags && object.tags.has('index-table-root')) tags.add('index-table-array');
      }
      return known(value, unionSources(object, property), tags);
    }

    if (ts.isPropertyAccessExpression(node)) {
      const object = evaluate(node.expression, depth + 1);
      if (!object.known) return UNKNOWN;
      const property = node.name.text;
      if (property === 'length' && (typeof object.value === 'string' || Array.isArray(object.value))) {
        return known(object.value.length, object.sources);
      }
      if (object.value && typeof object.value === 'object' && own(object.value, property)) {
        return known(object.value[property], object.sources);
      }
      return UNKNOWN;
    }

    if (ts.isPrefixUnaryExpression(node)) {
      const operand = evaluate(node.operand, depth + 1);
      if (!operand.known) return UNKNOWN;
      switch (node.operator) {
        case ts.SyntaxKind.PlusToken: return known(+operand.value, operand.sources);
        case ts.SyntaxKind.MinusToken: return known(-operand.value, operand.sources);
        case ts.SyntaxKind.ExclamationToken: return known(!operand.value, operand.sources);
        case ts.SyntaxKind.TildeToken: return known(~operand.value, operand.sources);
        default: return UNKNOWN;
      }
    }

    if (ts.isTypeOfExpression(node)) {
      const operand = evaluate(node.expression, depth + 1);
      return operand.known ? known(typeof operand.value, operand.sources) : UNKNOWN;
    }
    if (ts.isVoidExpression(node)) {
      const operand = evaluate(node.expression, depth + 1);
      return operand.known ? known(undefined, operand.sources) : UNKNOWN;
    }

    if (ts.isConditionalExpression(node)) {
      const condition = evaluate(node.condition, depth + 1);
      if (!condition.known) return UNKNOWN;
      const selected = condition.value ? node.whenTrue : node.whenFalse;
      const value = evaluate(selected, depth + 1);
      return value.known ? known(value.value, unionSources(condition, value)) : UNKNOWN;
    }

    if (ts.isTemplateExpression(node)) {
      let text = node.head.text;
      const pieces = [known(text, ['literal'])];
      for (const span of node.templateSpans) {
        const expression = evaluate(span.expression, depth + 1);
        if (!expression.known) return UNKNOWN;
        text += String(expression.value) + span.literal.text;
        pieces.push(expression, known(span.literal.text, ['literal']));
      }
      return known(text, unionSources(...pieces));
    }

    if (ts.isBinaryExpression(node) && !isAssignmentOperator(node.operatorToken.kind)) {
      const left = evaluate(node.left, depth + 1);
      if (!left.known) return UNKNOWN;
      if (node.operatorToken.kind === ts.SyntaxKind.AmpersandAmpersandToken) {
        if (!left.value) return known(left.value, left.sources);
        const right = evaluate(node.right, depth + 1);
        return right.known ? known(right.value, unionSources(left, right)) : UNKNOWN;
      }
      if (node.operatorToken.kind === ts.SyntaxKind.BarBarToken) {
        if (left.value) return known(left.value, left.sources);
        const right = evaluate(node.right, depth + 1);
        return right.known ? known(right.value, unionSources(left, right)) : UNKNOWN;
      }
      if (node.operatorToken.kind === ts.SyntaxKind.QuestionQuestionToken) {
        if (left.value !== null && left.value !== undefined) return known(left.value, left.sources);
        const right = evaluate(node.right, depth + 1);
        return right.known ? known(right.value, unionSources(left, right)) : UNKNOWN;
      }
      const right = evaluate(node.right, depth + 1);
      if (!right.known) return UNKNOWN;
      let value;
      switch (node.operatorToken.kind) {
        case ts.SyntaxKind.PlusToken: value = left.value + right.value; break;
        case ts.SyntaxKind.MinusToken: value = left.value - right.value; break;
        case ts.SyntaxKind.AsteriskToken: value = left.value * right.value; break;
        case ts.SyntaxKind.SlashToken: value = left.value / right.value; break;
        case ts.SyntaxKind.PercentToken: value = left.value % right.value; break;
        case ts.SyntaxKind.AsteriskAsteriskToken: value = left.value ** right.value; break;
        case ts.SyntaxKind.LessThanLessThanToken: value = left.value << right.value; break;
        case ts.SyntaxKind.GreaterThanGreaterThanToken: value = left.value >> right.value; break;
        case ts.SyntaxKind.GreaterThanGreaterThanGreaterThanToken: value = left.value >>> right.value; break;
        case ts.SyntaxKind.AmpersandToken: value = left.value & right.value; break;
        case ts.SyntaxKind.BarToken: value = left.value | right.value; break;
        case ts.SyntaxKind.CaretToken: value = left.value ^ right.value; break;
        case ts.SyntaxKind.EqualsEqualsToken: value = left.value == right.value; break; // intentional JS semantics
        case ts.SyntaxKind.ExclamationEqualsToken: value = left.value != right.value; break; // intentional JS semantics
        case ts.SyntaxKind.EqualsEqualsEqualsToken: value = left.value === right.value; break;
        case ts.SyntaxKind.ExclamationEqualsEqualsToken: value = left.value !== right.value; break;
        case ts.SyntaxKind.LessThanToken: value = left.value < right.value; break;
        case ts.SyntaxKind.LessThanEqualsToken: value = left.value <= right.value; break;
        case ts.SyntaxKind.GreaterThanToken: value = left.value > right.value; break;
        case ts.SyntaxKind.GreaterThanEqualsToken: value = left.value >= right.value; break;
        default: return UNKNOWN;
      }
      return known(value, unionSources(left, right));
    }

    if (ts.isCallExpression(node)) {
      const args = [];
      const argValues = [];
      for (const argument of node.arguments) {
        if (ts.isSpreadElement(argument)) return UNKNOWN;
        const value = evaluate(argument, depth + 1);
        if (!value.known) return UNKNOWN;
        args.push(value.value);
        argValues.push(value);
      }

      if (ts.isIdentifier(node.expression) && !getResolvedSymbol(checker, node.expression)) {
        let value;
        switch (node.expression.text) {
          case 'String': value = String(...args); break;
          case 'Number': value = Number(...args); break;
          case 'Boolean': value = Boolean(...args); break;
          case 'parseInt': value = parseInt(...args); break;
          case 'parseFloat': value = parseFloat(...args); break;
          default: return UNKNOWN;
        }
        return known(value, unionSources(...argValues));
      }

      let receiverNode;
      let methodNode;
      if (ts.isPropertyAccessExpression(node.expression)) {
        receiverNode = node.expression.expression;
        methodNode = known(node.expression.name.text, ['literal']);
      } else if (ts.isElementAccessExpression(node.expression)) {
        receiverNode = node.expression.expression;
        methodNode = evaluate(node.expression.argumentExpression, depth + 1);
      } else return UNKNOWN;

      const receiver = evaluate(receiverNode, depth + 1);
      if (!receiver.known || !methodNode.known || typeof methodNode.value !== 'string') return UNKNOWN;
      const method = methodNode.value;
      let value;
      try {
        if (typeof receiver.value === 'string' && pureStringMethods.has(method)) {
          value = String.prototype[method].apply(receiver.value, args);
        } else if (Array.isArray(receiver.value) && pureArrayMethods.has(method)) {
          value = Array.prototype[method].apply(receiver.value, args);
        } else return UNKNOWN;
      } catch (_) {
        return UNKNOWN;
      }
      return known(value, unionSources(receiver, methodNode, ...argValues));
    }

    return UNKNOWN;
  }

  return evaluate;
}

function isIdentifierReference(sourceFile, node) {
  if (!ts.isIdentifier(node)) return false;
  const parent = node.parent;
  if (!parent) return true;

  if (
    (ts.isVariableDeclaration(parent) && parent.name === node) ||
    (ts.isParameter(parent) && parent.name === node) ||
    (ts.isFunctionDeclaration(parent) && parent.name === node) ||
    (ts.isFunctionExpression(parent) && parent.name === node) ||
    (ts.isClassDeclaration(parent) && parent.name === node) ||
    (ts.isClassExpression(parent) && parent.name === node) ||
    (ts.isBindingElement(parent) && parent.name === node) ||
    (ts.isLabeledStatement(parent) && parent.label === node) ||
    ((ts.isBreakStatement(parent) || ts.isContinueStatement(parent)) && parent.label === node)
  ) return false;

  if (ts.isPropertyAccessExpression(parent) && parent.name === node) return false;
  if (
    (ts.isPropertyAssignment(parent) || ts.isMethodDeclaration(parent) || ts.isGetAccessor(parent) ||
      ts.isSetAccessor(parent) || ts.isPropertyDeclaration(parent)) &&
    parent.name === node && !ts.isComputedPropertyName(parent.name)
  ) return false;
  if (ts.isShorthandPropertyAssignment(parent) && parent.name === node) return false;
  if (ts.isBinaryExpression(parent) && isAssignmentOperator(parent.operatorToken.kind) && parent.left === node) return false;
  if (
    (ts.isPrefixUnaryExpression(parent) || ts.isPostfixUnaryExpression(parent)) &&
    parent.operand === node &&
    (parent.operator === ts.SyntaxKind.PlusPlusToken || parent.operator === ts.SyntaxKind.MinusMinusToken)
  ) return false;

  return node.getStart(sourceFile) >= 0;
}

function isCandidateExpression(node) {
  return ts.isIdentifier(node) ||
    ts.isElementAccessExpression(node) ||
    ts.isPropertyAccessExpression(node) ||
    ts.isParenthesizedExpression(node) ||
    ts.isBinaryExpression(node) ||
    ts.isConditionalExpression(node) ||
    ts.isTemplateExpression(node) ||
    ts.isCallExpression(node);
}

function renderStringLiteral(value) {
  return JSON.stringify(value)
    .replace(/\u2028/g, '\\u2028')
    .replace(/\u2029/g, '\\u2029');
}

function sourcesToArray(sources) {
  return Array.from(sources).sort();
}

function makeRuntimeSeedBindings(sourceFile, checker, runtimeValues, prefixEnd, writes) {
  const bindings = new Map();
  for (const statement of sourceFile.statements) {
    if (statement.getStart(sourceFile) >= prefixEnd) break;
    if (!ts.isVariableStatement(statement)) continue;
    for (const declaration of statement.declarationList.declarations) {
      if (!ts.isIdentifier(declaration.name)) continue;
      const name = declaration.name.text;
      if (!own(runtimeValues, name)) continue;
      const symbol = getResolvedSymbol(checker, declaration.name);
      if (!symbol) continue;
      const laterWrites = (writes.get(symbol) || []).filter(position => position >= prefixEnd);
      if (laterWrites.length > 0) continue;
      const value = runtimeValues[name];
      let source;
      if (name === 'hr') source = 'string-table';
      else if (name === 'hu') source = 'index-table';
      else if (typeof value === 'string') source = 'runtime-string';
      else source = 'runtime-scalar';
      const tags = name === 'hr' ? ['string-table-root'] : (name === 'hu' ? ['index-table-root'] : []);
      bindings.set(symbol, {
        name,
        availableFrom: prefixEnd,
        staticValue: known(value, [source], tags),
        origin: 'runtime-prefix',
        declaration,
      });
    }
  }
  return bindings;
}

function addImmutableBindings(sourceFile, checker, bindings, writes, declarators, prefixEnd) {
  let passes = 0;
  let addedTotal = 0;
  for (; passes < 20; passes += 1) {
    let addedThisPass = 0;
    const evaluate = createStaticEvaluator(sourceFile, checker, bindings);
    for (const item of declarators) {
      const { node, symbol, name } = item;
      if (node.getStart(sourceFile) < prefixEnd || bindings.has(symbol)) continue;
      if ((symbol.declarations || []).length !== 1) continue;
      if ((writes.get(symbol) || []).length > 0) continue;
      const value = evaluate(node.initializer);
      if (!value.known) continue;
      const cloned = cloneSerializable(value.value);
      if (cloned === undefined && value.value !== undefined) continue;
      bindings.set(symbol, {
        name,
        availableFrom: node.end,
        staticValue: known(cloned, value.sources, value.tags),
        origin: 'immutable-local',
        declaration: node,
      });
      addedThisPass += 1;
      addedTotal += 1;
    }
    if (addedThisPass === 0) break;
  }
  return { passes: passes + 1, addedTotal };
}

function buildTaintMap(sourceFile, checker, bindings, declarators) {
  const taint = new Map();
  for (const [symbol, binding] of bindings) {
    taint.set(symbol, new Set(binding.staticValue.sources));
  }

  const dependencies = new Map();
  for (const { node, symbol } of declarators) {
    const deps = new Set();
    function visit(current) {
      if (ts.isIdentifier(current)) {
        const dependency = getResolvedSymbol(checker, current);
        if (dependency && dependency !== symbol) deps.add(dependency);
      }
      ts.forEachChild(current, visit);
    }
    visit(node.initializer);
    dependencies.set(symbol, deps);
  }

  for (let pass = 0; pass < 20; pass += 1) {
    let changed = false;
    for (const [symbol, deps] of dependencies) {
      let existing = taint.get(symbol);
      if (!existing) existing = new Set();
      const before = existing.size;
      for (const dependency of deps) {
        const dependencySources = taint.get(dependency);
        if (!dependencySources) continue;
        for (const source of dependencySources) existing.add(source);
      }
      if (existing.size > 0 && (!taint.has(symbol) || existing.size !== before)) {
        taint.set(symbol, existing);
        changed = true;
      }
    }
    if (!changed) break;
  }
  return taint;
}

function containsTaint(sourceFile, checker, taint, node, wanted) {
  let found = false;
  function visit(current) {
    if (found) return;
    if (ts.isIdentifier(current)) {
      const symbol = getResolvedSymbol(checker, current);
      const sources = symbol && taint.get(symbol);
      if (sources && Array.from(wanted).some(source => sources.has(source))) {
        found = true;
        return;
      }
    }
    ts.forEachChild(current, visit);
  }
  visit(node);
  return found;
}

function main() {
  const options = parseArgs(process.argv.slice(2));
  const inputPath = path.resolve(options.input);
  const outputPath = path.resolve(options.output);
  const reportPath = path.resolve(options.report);
  const mapPath = path.resolve(options.map);
  const unresolvedPath = path.resolve(options.unresolved);
  const runtimeValuesPath = options.runtimeValues ? path.resolve(options.runtimeValues) : null;

  const sourceBuffer = fs.readFileSync(inputPath);
  const inputSha256 = sha256(sourceBuffer);
  if (inputSha256 !== EXPECTED_BUNDLE_SHA256) {
    throw new Error(`Immutable baseline mismatch: expected ${EXPECTED_BUNDLE_SHA256}, got ${inputSha256}`);
  }
  const sourceText = sourceBuffer.toString('utf8');
  const lineStarts = buildLineStarts(sourceText);
  const prefixEnd = offsetAtLine(lineStarts, options.runtimeEvalEndLine + 1, sourceText.length);
  const replacementStart = offsetAtLine(lineStarts, options.replacementStartLine, sourceText.length);
  const prefixSource = sourceText.slice(0, prefixEnd);
  const prefixSha256 = sha256(prefixSource);
  if (prefixSha256 !== EXPECTED_RUNTIME_PREFIX_SHA256) {
    throw new Error(`Runtime-prefix baseline mismatch: expected ${EXPECTED_RUNTIME_PREFIX_SHA256}, got ${prefixSha256}`);
  }

  console.error('[decode] evaluating isolated runtime prefix');
  const runtimeValues = evaluateRuntimePrefix(prefixSource, `${path.basename(inputPath)}:1-${options.runtimeEvalEndLine}`);
  if (!Array.isArray(runtimeValues.hr) || runtimeValues.hr.length !== 49) {
    throw new Error('Runtime string table `hr` was not recovered as 49 arrays');
  }
  if (!Array.isArray(runtimeValues.hu)) {
    throw new Error('Runtime index table `hu` was not recovered');
  }

  console.error('[decode] parsing and binding complete bundle');
  const { program, sourceFile, checker } = createProgram(inputPath, sourceText);
  const syntaxDiagnostics = program.getSyntacticDiagnostics(sourceFile);
  if (syntaxDiagnostics.length > 0) {
    const first = syntaxDiagnostics[0];
    throw new Error(`Input parse failed: ${ts.flattenDiagnosticMessageText(first.messageText, '\n')}`);
  }

  console.error('[decode] collecting writes and declarations');
  const writes = collectWrites(sourceFile, checker);
  const declarators = collectVariableDeclarators(sourceFile, checker);
  const bindings = makeRuntimeSeedBindings(sourceFile, checker, runtimeValues, prefixEnd, writes);
  const runtimeBindingCount = bindings.size;
  console.error(`[decode] runtime bindings: ${runtimeBindingCount}; propagating immutable aliases`);
  const propagation = addImmutableBindings(sourceFile, checker, bindings, writes, declarators, prefixEnd);
  const evaluate = createStaticEvaluator(sourceFile, checker, bindings);
  console.error(`[decode] recovered static bindings: ${bindings.size}; building taint graph`);
  const taint = buildTaintMap(sourceFile, checker, bindings, declarators);

  const stringTableMutationSites = [];
  const mutatingArrayMethods = new Set(['copyWithin', 'fill', 'pop', 'push', 'reverse', 'shift', 'sort', 'splice', 'unshift']);
  function recordStringTableMutation(node, operation) {
    const start = node.getStart(sourceFile);
    if (start < replacementStart) return;
    stringTableMutationSites.push({
      operation,
      sourceRange: {
        startOffset: start,
        endOffset: node.end,
        start: locationAt(lineStarts, start),
        end: locationAt(lineStarts, node.end),
      },
      originalText: sourceText.slice(start, node.end),
    });
  }
  function scanStringTableMutations(node) {
    if (ts.isBinaryExpression(node) && isAssignmentOperator(node.operatorToken.kind)) {
      const target = node.left;
      if (ts.isElementAccessExpression(target) || ts.isPropertyAccessExpression(target)) {
        const receiver = evaluate(target.expression);
        if (receiver.known && receiver.tags && (receiver.tags.has('string-table-root') || receiver.tags.has('string-table-array'))) {
          recordStringTableMutation(node, 'member-assignment');
        }
      }
    } else if (
      (ts.isPrefixUnaryExpression(node) || ts.isPostfixUnaryExpression(node)) &&
      (node.operator === ts.SyntaxKind.PlusPlusToken || node.operator === ts.SyntaxKind.MinusMinusToken) &&
      (ts.isElementAccessExpression(node.operand) || ts.isPropertyAccessExpression(node.operand))
    ) {
      const receiver = evaluate(node.operand.expression);
      if (receiver.known && receiver.tags && (receiver.tags.has('string-table-root') || receiver.tags.has('string-table-array'))) {
        recordStringTableMutation(node, 'member-update');
      }
    } else if (ts.isCallExpression(node)) {
      let receiverNode = null;
      let methodName = null;
      if (ts.isPropertyAccessExpression(node.expression)) {
        receiverNode = node.expression.expression;
        methodName = node.expression.name.text;
      } else if (ts.isElementAccessExpression(node.expression)) {
        receiverNode = node.expression.expression;
        const methodValue = evaluate(node.expression.argumentExpression);
        if (methodValue.known && typeof methodValue.value === 'string') methodName = methodValue.value;
      }
      if (receiverNode && methodName && mutatingArrayMethods.has(methodName)) {
        const receiver = evaluate(receiverNode);
        if (receiver.known && receiver.tags && (receiver.tags.has('string-table-root') || receiver.tags.has('string-table-array'))) {
          recordStringTableMutation(node, `array-mutator:${methodName}`);
        }
      }
    }
    ts.forEachChild(node, scanStringTableMutations);
  }
  scanStringTableMutations(sourceFile);
  if (stringTableMutationSites.length > 0) {
    throw new Error(`Safety invariant failed: ${stringTableMutationSites.length} post-runtime string-table mutation(s) detected`);
  }

  console.error('[decode] collecting confirmed string replacements');
  const candidateMap = new Map();
  function collectCandidates(node) {
    const start = node.getStart(sourceFile);
    if (start >= replacementStart && isCandidateExpression(node)) {
      if (!ts.isIdentifier(node) || isIdentifierReference(sourceFile, node)) {
        const value = evaluate(node);
        if (value.known && typeof value.value === 'string' && hasStringRuntimeSource(value)) {
          const replacement = renderStringLiteral(value.value);
          const original = sourceText.slice(start, node.end);
          if (replacement !== original) {
            candidateMap.set(node, {
              node,
              start,
              end: node.end,
              original,
              replacement,
              decodedValue: value.value,
              sources: sourcesToArray(value.sources),
              kind: ts.SyntaxKind[node.kind],
            });
          }
        }
      }
    }
    ts.forEachChild(node, collectCandidates);
  }
  collectCandidates(sourceFile);

  const selected = [];
  for (const candidate of candidateMap.values()) {
    let parent = candidate.node.parent;
    let shadowedByParent = false;
    while (parent && parent.getStart(sourceFile) >= replacementStart) {
      if (candidateMap.has(parent)) {
        shadowedByParent = true;
        break;
      }
      parent = parent.parent;
    }
    if (!shadowedByParent) selected.push(candidate);
  }
  selected.sort((a, b) => a.start - b.start || b.end - a.end);

  for (let i = 1; i < selected.length; i += 1) {
    if (selected[i].start < selected[i - 1].end) {
      throw new Error(`Overlapping replacements at ${selected[i - 1].start} and ${selected[i].start}`);
    }
  }

  console.error(`[decode] selected replacements before overlap filtering: ${selected.length}`);
  const unresolved = [];
  const unresolvedSeen = new Set();
  const wantedStringTaint = new Set(['string-table', 'runtime-string']);
  function addUnresolved(node, reason, details = {}) {
    const start = node.getStart(sourceFile);
    if (start < replacementStart) return;
    const key = `${start}:${node.end}:${reason}`;
    if (unresolvedSeen.has(key)) return;
    unresolvedSeen.add(key);
    unresolved.push({
      sourceRange: {
        startOffset: start,
        endOffset: node.end,
        start: locationAt(lineStarts, start),
        end: locationAt(lineStarts, node.end),
      },
      reason,
      expressionKind: ts.SyntaxKind[node.kind],
      originalText: sourceText.slice(start, node.end),
      ...details,
    });
  }

  function collectUnresolved(node) {
    const start = node.getStart(sourceFile);
    if (start >= replacementStart) {
      if (ts.isElementAccessExpression(node)) {
        const objectValue = evaluate(node.expression);
        const indexValue = evaluate(node.argumentExpression);
        const wholeValue = evaluate(node);
        if (objectValue.known && objectValue.tags && (objectValue.tags.has('string-table-root') || objectValue.tags.has('string-table-array')) && !wholeValue.known) {
          addUnresolved(node, indexValue.known ? 'string-table-index-out-of-range' : 'dynamic-string-table-index');
        } else if (!indexValue.known && containsTaint(sourceFile, checker, taint, node.argumentExpression, wantedStringTaint)) {
          addUnresolved(node.argumentExpression, 'dynamic-runtime-derived-property-key');
        }
      }
      if (ts.isIdentifier(node) && isIdentifierReference(sourceFile, node)) {
        const symbol = getResolvedSymbol(checker, node);
        const sources = symbol && taint.get(symbol);
        if (sources && (sources.has('string-table') || sources.has('runtime-string')) && !bindings.has(symbol)) {
          addUnresolved(node, 'mutable-or-unsupported-runtime-derived-binding', {
            symbol: node.text,
            taintSources: Array.from(sources).sort(),
          });
        }
      }
    }
    ts.forEachChild(node, collectUnresolved);
  }
  collectUnresolved(sourceFile);
  unresolved.sort((a, b) => a.sourceRange.startOffset - b.sourceRange.startOffset);

  console.error(`[decode] unresolved candidates: ${unresolved.length}; applying replacements`);
  const outputParts = [];
  let outputCursor = 0;
  for (const item of selected) {
    outputParts.push(sourceText.slice(outputCursor, item.start), item.replacement);
    outputCursor = item.end;
  }
  outputParts.push(sourceText.slice(outputCursor));
  const outputText = outputParts.join('');

  if (outputText.slice(0, replacementStart) !== sourceText.slice(0, replacementStart)) {
    throw new Error('Safety invariant failed: protected prefix/anti-tamper range changed');
  }
  const outputLineStarts = buildLineStarts(outputText);
  if (outputLineStarts.length !== lineStarts.length) {
    throw new Error('Safety invariant failed: output logical line count changed');
  }

  console.error('[decode] validating transformed JavaScript');
  const outputProgram = createProgram(outputPath, outputText);
  const outputDiagnostics = outputProgram.program.getSyntacticDiagnostics(outputProgram.sourceFile);
  if (outputDiagnostics.length > 0) {
    const first = outputDiagnostics[0];
    throw new Error(`Output parse failed: ${ts.flattenDiagnosticMessageText(first.messageText, '\n')}`);
  }

  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.mkdirSync(path.dirname(reportPath), { recursive: true });
  fs.writeFileSync(outputPath, outputText, 'utf8');

  const replacementByKind = Object.create(null);
  const replacementBySource = Object.create(null);
  const uniqueStrings = new Set();
  const mapLines = [];
  for (let index = 0; index < selected.length; index += 1) {
    const item = selected[index];
    replacementByKind[item.kind] = (replacementByKind[item.kind] || 0) + 1;
    for (const source of item.sources) replacementBySource[source] = (replacementBySource[source] || 0) + 1;
    uniqueStrings.add(item.decodedValue);
    mapLines.push(JSON.stringify({
      id: index + 1,
      sourceRange: {
        startOffset: item.start,
        endOffset: item.end,
        start: locationAt(lineStarts, item.start),
        end: locationAt(lineStarts, item.end),
      },
      expressionKind: item.kind,
      originalText: item.original,
      replacementText: item.replacement,
      decodedValue: item.decodedValue,
      provenance: item.sources,
      confidence: 'CONFIRMED',
      proof: 'Pure static expression resolved from isolated runtime prefix/string table',
    }));
  }
  fs.writeFileSync(mapPath, mapLines.join('\n') + (mapLines.length ? '\n' : ''), 'utf8');
  fs.writeFileSync(unresolvedPath, unresolved.map(item => JSON.stringify(item)).join('\n') + (unresolved.length ? '\n' : ''), 'utf8');

  const unresolvedByReason = Object.create(null);
  for (const item of unresolved) unresolvedByReason[item.reason] = (unresolvedByReason[item.reason] || 0) + 1;

  const runtimeValueSummary = {
    status: 'CONFIRMED',
    evaluatedRange: `bundle.js:1-${options.runtimeEvalEndLine}`,
    stringTableCount: runtimeValues.hr.length,
    stringEntryCount: runtimeValues.hr.reduce((sum, table) => sum + table.length, 0),
    indexTableLength: runtimeValues.hu.length,
    capturedPrimitiveOrArrayGlobals: Object.keys(runtimeValues).length,
    selectedValues: Object.fromEntries(
      ['ck', 'cl', 'cm', 'cn', 'dY', 'd5', 'eb', 'ef', 'fw', 'fB', 'fM', 'gF', 'g9']
        .filter(key => own(runtimeValues, key))
        .map(key => [key, runtimeValues[key]]),
    ),
  };
  if (runtimeValuesPath) {
    fs.writeFileSync(runtimeValuesPath, JSON.stringify(runtimeValueSummary, null, 2) + '\n', 'utf8');
  }

  const report = {
    schemaVersion: 1,
    status: 'PARTIAL',
    input: {
      path: path.relative(process.cwd(), inputPath),
      sizeBytes: sourceBuffer.length,
      sha256: sha256(sourceBuffer),
      newlineCount: (sourceText.match(/\n/g) || []).length,
      logicalLineCount: lineStarts.length,
      endsWithNewline: sourceText.endsWith('\n'),
    },
    output: {
      path: path.relative(process.cwd(), outputPath),
      sizeBytes: Buffer.byteLength(outputText),
      sha256: sha256(outputText),
      newlineCount: (outputText.match(/\n/g) || []).length,
      logicalLineCount: outputLineStarts.length,
      syntaxValid: true,
    },
    safety: {
      fullBundleExecuted: false,
      inputHashPinned: true,
      expectedInputSha256: EXPECTED_BUNDLE_SHA256,
      runtimePrefixHashPinned: true,
      expectedRuntimePrefixSha256: EXPECTED_RUNTIME_PREFIX_SHA256,
      executedRange: `bundle.js:1-${options.runtimeEvalEndLine}`,
      executedEnvironment: 'Hash-pinned Node vm with dynamic code generation disabled and Math.random blocked',
      unavailableGlobals: ['wx', 'tt', 'Laya', 'require', 'process', 'fetch', 'XMLHttpRequest', 'WebSocket', 'Date', 'performance', 'timers', 'Function', 'eval', 'console'],
      antiTamperRange: `bundle.js:${options.runtimeEvalEndLine + 1}-${options.replacementStartLine - 1}`,
      antiTamperPreservedByteForByte: true,
      replacementRangeStart: `bundle.js:${options.replacementStartLine}:0`,
      lineCountPreserved: true,
      postRuntimeStringTableMutations: stringTableMutationSites,
      postRuntimeStringTableImmutable: stringTableMutationSites.length === 0,
    },
    runtime: runtimeValueSummary,
    staticAnalysis: {
      runtimeSeedBindings: runtimeBindingCount,
      immutableBindingsRecovered: propagation.addedTotal,
      propagationPasses: propagation.passes,
      totalBindingsWithStaticValues: bindings.size,
    },
    replacements: {
      count: selected.length,
      uniqueDecodedStrings: uniqueStrings.size,
      byExpressionKind: replacementByKind,
      byProvenance: replacementBySource,
      fullMap: path.relative(path.dirname(reportPath), mapPath),
      samples: selected.slice(0, 30).map(item => ({
        sourceRange: `${locationAt(lineStarts, item.start).line}:${locationAt(lineStarts, item.start).column}-${locationAt(lineStarts, item.end).line}:${locationAt(lineStarts, item.end).column}`,
        originalText: item.original,
        decodedValue: item.decodedValue,
        expressionKind: item.kind,
      })),
    },
    unresolved: {
      count: unresolved.length,
      byReason: unresolvedByReason,
      fullList: path.relative(path.dirname(reportPath), unresolvedPath),
      interpretation: 'These positions remain unchanged; no value was guessed.',
    },
    limitations: [
      'No control-flow branch was removed or simplified in this stage.',
      'No mutable binding was replaced solely from a guessed final value.',
      'No scene, network, platform, battle, or Laya code was executed.',
      'A missing external bundle.js.map prevents direct original-source recovery.',
    ],
  };
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n', 'utf8');

  process.stdout.write(JSON.stringify({
    ok: true,
    inputSha256: report.input.sha256,
    outputSha256: report.output.sha256,
    replacements: selected.length,
    uniqueDecodedStrings: uniqueStrings.size,
    unresolved: unresolved.length,
    antiTamperPreserved: true,
  }, null, 2) + '\n');
}

try {
  main();
} catch (error) {
  console.error(error && error.stack ? error.stack : String(error));
  process.exitCode = 1;
}
