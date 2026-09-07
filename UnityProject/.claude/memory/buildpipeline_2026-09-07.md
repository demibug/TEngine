# Build pipeline test reconciliation to full green (94/94 EditMode)

## Context

The uncommitted release-tools refactor (BuildPreflight / BuildStageImpl / DllArtifactCopier /
MinimalPackageProcessor / BuildExecutionResult / ReleaseTools.ExecuteBuildWithResult) plus
Assets/Tests/BuildPipeline/EditMode had 10 failing tests (94 total).

## Root causes and fixes

1. ResolveOutputRoot (ReleaseTools.cs) normalized separators only for relative paths, so
   absolute OutputRoot kept backslashes while the test contract (both tests together) implies:
   GetFullPath-normalized with '/' separators, project-rooted only when relative. Fix: uniform
   Path.GetFullPath(outputRoot).Replace('\\','/') for both branches.

2. Three tests (AbFailure_StopsMinimalAndPlayer, MinimalPackageDisabled_MinimalStageSkipped,
   MinimalPackageFailure_StopsPlayer) expected pure ["AB"] stage calls for BuildHotFixDll=false,
   but the intended design (ReuseValidationRunsInsteadOfCompile, ReuseValidationFailure_BlocksAb,
   HybridClrUnavailable_SkipsDllStage) is: reuse validation runs instead of compile when
   HybridClr is available. Fix: expectations updated to ["ReuseCheck","AB"].

3. Failure-path tests didn't expect the orchestration's legitimate Fail() LogError
   (ReleaseTools.cs:~350), which the Unity Test Framework treats as unhandled error logs.
   Fix: LogAssert.Expect(LogType.Error, new Regex(@"构建失败")) before the Execute lines
   (PreflightFail, AbFailure, AbThrows, MinimalPackageFailure, PlayerFailed, PlayerNullOutcome,
   PlayerUnknownOutcome, ReuseValidationFailure_BlocksAb). PlayerCancelled needs none (no Fail call).

## Test infrastructure notes

- Unity batchmode may exit 2 instantly with no log/results right after a Hub-launched editor
  started (licensing/ILPP settling). Retrying with the explicit Unity.exe path works.
- Editor.log / Licensing logs are locked by a running editor; open with FileShare.ReadWrite.
- The project baseline files are UTF-8 without BOM, LF line endings (the CR found in working
  copies came from in-memory strings, not files).
- PowerShell: never echo inside a helper whose return value is captured by assignment
  ($c = Helper  ...) - Write-Output goes to the same output stream and the echo is
  concatenated into the captured value; it was persisted into a source file and had to be
  repaired. Use inline logic or a [void]/stream-safe helper.

## Result

Unity 2022.3.62f2 EditMode: result="Passed" total="94" passed="94" failed="0" (exit 0).