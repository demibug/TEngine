! function(e, t) {
  "use strict";
  window.ImageData || (window.ImageData = function() {
    let e, s, i;
    if (3 == arguments.length) {
      if (!(arguments[0] instanceof Uint8ClampedArray)) throw new Error("Failed to construct 'ImageData': parameter 1 is not of type 'Uint8ClampedArray'.");
      if (arguments[0].length % 4 != 0) throw new Error("Failed to construct 'ImageData': The input data length is not a multiple of 4.");
      if (arguments[0].length !== arguments[1] * arguments[2] * 4) throw new Error("Failed to construct 'ImageData': The input data length is not equal to (4 * width * height).");
      i = arguments[0], e = arguments[1], s = arguments[2]
    } else if (2 == arguments.length) e = arguments[0], s = arguments[1], i = new Uint8ClampedArray(arguments[0] * arguments[1] * 4);
    else if (arguments.length < 2) throw new Error("Failed to construct 'ImageData': 2 arguments required, but only " + arguments.length + " present.");
    let r = t.Browser.canvas.context.getImageData(0, 0, e, s),
      a = r.data;
    for (let e = 0, t = i.length; e < t; e++) a[e] = i[e];
    return r
  });
  class s {
    constructor(e) {
      this.totalFileSize = 0, this.running = !1, this.lastGroup = -1, this.lastGroupUsed = 0, this.toClear = 0, this.cacheRoot = e, this.fileCache = new Map, this.cacheGroups = new Array(5), this.cacheGroups.fill(0), this.cacheRequest = [], this.toSaveManifestFlags = new Array(5)
    }
    start() {
      return this.checkAndDeleteOldCacheDir(), this.createCacheDirs().then(() => this.loadAllManifests()).then(() => {
        t.ILaya.systemTimer.loop(s.processInterval, this, this.process)
      })
    }
    getFile(e) {
      let s = this.fileCache.get(e);
      if (!s) return Promise.resolve(null);
      s.accessTime = this.toSaveManifestRequest = performance.now(), this.toSaveManifestFlags[s.group] = !0;
      let i = `${this.cacheRoot}/${s.group}/${s.fileName}`;
      return t.PAL.fs.exists(i).then(e => e ? i : null)
    }
    addFile(e, s) {
      t.PAL.fs.getFileSize(s).then(t => this.cacheRequest.push({
        url: e,
        tempFilePath: s,
        size: t
      })).catch(e => {
        console.warn("[Cache]get file size error", t.getErrorMsg(e))
      })
    }
    clearAllCache() {
      this.toClear = 2
    }
    process() {
      if (this.running) return;
      if (0 != this.toClear) {
        this.running = !0;
        let e = this.toClear;
        return this.toClear = 0, void(2 & e ? this.doClearAllCache().then(() => this.running = !1) : this.clearSpace(0).then(() => this.saveDirtyManifests()).then(() => this.running = !1))
      }
      if (0 === this.cacheRequest.length) return void(null != this.toSaveManifestRequest && performance.now() - this.toSaveManifestRequest > s.saveAccessTimeInterval && (this.toSaveManifestRequest = null, this.running = !0, this.saveDirtyManifests().then(() => this.running = !1)));
      this.running = !0;
      let e = this.cacheRequest.concat();
      this.cacheRequest.length = 0;
      let t = this.selectGroup(e.length),
        i = 0;
      for (let t of e) i += t.size;
      let r = this.totalFileSize + i - s.spaceLimit;
      this.clearSpace(r).then(() => this.addFilesToCache(e, t)).then(() => this.saveDirtyManifests()).then(() => this.running = !1)
    }
    selectGroup(e) {
      if (this.lastGroup >= 0 && this.lastGroupUsed < 50) return this.lastGroupUsed += e, this.lastGroup;
      let t, s = Number.MAX_VALUE;
      for (let e = 0; e < 5; e++) this.cacheGroups[e] < s - 50 && (s = this.cacheGroups[e], t = e);
      return this.lastGroup = t, this.lastGroupUsed = e, t
    }
    addFilesToCache(e, s) {
      for (let {
          url: i,
          tempFilePath: r,
          size: a
        }
        of e) {
        let e = this.fileCache.get(i);
        e ? (this.cacheGroups[e.group]--, this.totalFileSize -= e.size, this.fileCache.delete(i), e.accessTime = performance.now(), e.size = a) : e = {
          group: s,
          url: i,
          size: a,
          fileName: t.Utils.getBaseName(r),
          accessTime: performance.now()
        }, this.fileCache.set(i, e), this.cacheGroups[e.group]++, this.totalFileSize += a
      }
      return this.saveManifest(s).then(i => {
        if (i) return t.Utils.runTasks(e, 5, ({
          url: e,
          tempFilePath: i
        }) => {
          let r = this.fileCache.get(e),
            a = `${this.cacheRoot}/${s}/${r.fileName}`;
          return t.PAL.fs.copyFile(i, a).catch(e => {
            let s = t.getErrorMsg(e);
            console.warn("[Cache]create cache file", s), -1 !== s.indexOf("the maximum size of the file storage") && (this.toClear |= 1)
          })
        })
      })
    }
    clearSpace(e) {
      if (e < 0) return Promise.resolve();
      e += s.minClearSpace;
      let t = Array.from(this.fileCache.values());
      t.sort((e, t) => e.accessTime - t.accessTime);
      let i = performance.now(),
        r = [0, 0];
      return this.doClearSpace(t, e, 0, r).then(() => {
        console.log(`[Cache]cleared ${r[0]} files/${r[1]} bytes in ${performance.now()-i}ms`)
      })
    }
    doClearSpace(e, s, i, r) {
      let a = 0,
        o = 0,
        n = e.length;
      for (; o < n; o++) {
        let t = e[o];
        if (a += t.size, a >= s) break
      }
      let l = e.splice(0, o + 1);
      return a = 0, t.Utils.runTasks(l, 20, e => {
        this.fileCache.delete(e.url), this.totalFileSize -= e.size, this.cacheGroups[e.group]--, this.toSaveManifestFlags[e.group] = !0;
        let s = `${this.cacheRoot}/${e.group}/${e.fileName}`;
        return t.PAL.fs.unlink(s).then(() => {
          a += e.size
        }).catch(e => {
          let s = t.getErrorMsg(e); - 1 === s.indexOf("no such file") && console.error("[Cache]delete cache file", s)
        })
      }).then(() => (r[0] += l.length, r[1] += a, a < s && e.length > 0 && i < 10 ? this.doClearSpace(e, s - a, ++i, r) : null))
    }
    doClearAllCache() {
      return t.PAL.fs.rmdir(this.cacheRoot, {
        recursive: !0
      }).catch(e => {
        console.warn("[Cache]failed to delete cache folder", t.getErrorMsg(e))
      }).then(() => this.createCacheDirs()).then(() => {
        this.fileCache.clear(), this.cacheGroups.fill(0), this.totalFileSize = 0, this.toSaveManifestFlags.fill(!1)
      })
    }
    loadAllManifests() {
      return t.PAL.fs.readdir(this.cacheRoot).then(e => Promise.all(e.map(e => {
        if (e.startsWith("manifest-")) {
          let t = parseInt(e.substring(9, e.length - 4));
          if (!isNaN(t) && t >= 0 && t < 5) return this.loadManifest(t)
        }
        return Promise.resolve()
      })))
    }
    loadManifest(e) {
      return t.PAL.fs.readFile(`${this.cacheRoot}/manifest-${e}.bin`).then(s => {
        let i, r, a, o, n = new t.Byte(s);
        n.readInt16();
        let l = 0,
          h = 0;
        for (; n.bytesAvailable > 0;) {
          let t = n.pos,
            s = n.readUint32();
          i = n.readUTFString(), r = n.readUTFString(), a = n.readUint32(), o = n.readUint32(), n.pos = t + s;
          let c = {
            group: e,
            url: i,
            fileName: r,
            accessTime: a,
            size: o
          };
          this.fileCache.set(i, c), h += o, l++
        }
        this.cacheGroups[e] = l, this.totalFileSize += h, console.log(`[Cache]load manifest-${e} ${l}(files)/${h}(bytes)`)
      }).catch(s => {
        console.error(`[Cache]load manifest-${e}`, t.getErrorMsg(s))
      })
    }
    saveDirtyManifests() {
      return Promise.all(this.toSaveManifestFlags.filter(e => e).map((e, t) => this.saveManifest(t)))
    }
    saveManifest(e) {
      let s = new t.Byte;
      s.writeInt16(1);
      let i = 0,
        r = 0;
      for (let t of this.fileCache.values())
        if (t.group === e) {
          let e = s.pos;
          s.writeUint32(0), s.writeUTFString(t.url), s.writeUTFString(t.fileName), s.writeUint32(t.accessTime), s.writeUint32(t.size);
          let a = e;
          e = s.pos, s.pos = a, s.writeUint32(e - a), s.pos = e, i++, r += t.size
        } return t.PAL.fs.writeFile(`${this.cacheRoot}/manifest-${e}.bin`, s.buffer).then(() => (this.toSaveManifestFlags[e] = !1, console.log(`[Cache]save manifest-${e} ${i}(files)/${r}(bytes)`), !0)).catch(s => {
        let i = t.getErrorMsg(s);
        return console.error(`[Cache]save manifest-${e}`, i), -1 !== i.indexOf("the maximum size of the file storage") && (this.toClear |= 1), !1
      })
    }
    createCacheDirs() {
      return Promise.all(this.cacheGroups.map((e, s) => {
        let i = `${this.cacheRoot}/${s}`;
        return t.PAL.fs.exists(i).then(e => e ? Promise.resolve() : t.PAL.fs.mkdir(i, {
          recursive: !0
        }).catch(e => {
          console.error("[Cache]failed to create cache dir", t.getErrorMsg(e))
        }))
      }))
    }
    checkAndDeleteOldCacheDir() {
      let e = this.cacheRoot.substring(0, this.cacheRoot.lastIndexOf("/")) + "/layaairGame";
      return t.PAL.fs.exists(e).then(s => s ? (console.log("[Cache]delete old cache folder"), t.PAL.fs.rmdir(e, {
        recursive: !0
      }).catch(e => {
        console.warn("[Cache]failed to delete old cache folder", t.getErrorMsg(e))
      })) : Promise.resolve())
    }
  }
  s.minClearSpace = 5242880, s.spaceLimit = 209715200, s.processInterval = 2e3, s.saveAccessTimeInterval = 15e3;
  class i extends t.Downloader {
    constructor(e = !0) {
      super(), this.escapeZhCharsInURL = !0, this.supportSubPackageMultiLevelFolders = !0;
      let i = t.URL.postFormatURL;
      if (t.URL.postFormatURL = e => (e = this.checkSubpackagePrefix(e), i.call(this, e)), (t.Browser.onVVMiniGame || t.Browser.onQGMiniGame) && (this.supportSubPackageMultiLevelFolders = !1), (t.Browser.onWXMiniGame || t.Browser.onHWMiniGame) && (this.escapeZhCharsInURL = !1), e) {
        let e;
        e = t.Browser.onVVMiniGame ? "internal://files/layaCache" : t.PAL.g.env.USER_DATA_PATH + "/layaCache", this.cacheManager = new s(e)
      }
    }
    common(e, t, s, i, r, a) {
      t.startsWith("http://") || t.startsWith("https://") ? Promise.resolve().then(() => this.cacheManager ? this.cacheManager.getFile(t) : Promise.resolve(null)).then(e => {
        e ? "filePath" === i ? a(e) : this.readFile(e, i, a) : this.downloadFile(t, r, (e, t) => {
          e ? "filePath" === i ? a(e) : this.readFile(e, i, a) : a(null, t)
        })
      }) : "filePath" === i ? a(t) : this.readFile(t, i, a)
    }
    image(e, t, s, i, r) {
      (t.startsWith("http://") || t.startsWith("https://")) && this.cacheManager ? this.cacheManager.getFile(t).then(a => {
        a ? super.image(e, a, s, i, r) : this.downloadFile(t, i, (t, a) => {
          t ? super.image(e, t, s, i, r) : r(null, a)
        })
      }) : super.image(e, this.escapeURL(t), s, i, r)
    }
    package(e, s, i) {
      let r = e;
      this.supportSubPackageMultiLevelFolders || (r = e.replace(/\//g, "."), r !== e && (this.subPackages || (this.subPackages = {}), this.subPackages[e] = r));
      let a = {
        success: () => {
          i({
            loadScript: !1
          })
        },
        fail: e => {
          i({
            loadScript: !1
          }, t.getErrorMsg(e))
        },
        complete: null
      };
      t.Browser.onHWMiniGame ? a.subpackage = r : a.name = r;
      let o = t.PAL.g.loadSubpackage(a);
      s && o.onProgressUpdate && o.onProgressUpdate(e => s(e.progress))
    }
    downloadFile(e, s, i) {
      let r = t.PAL.g.downloadFile({
        url: this.escapeURL(e),
        success: s => {
          if (null == s.statusCode || 200 === s.statusCode) {
            let t = s.tempFilePath || s.apFilePath;
            this.cacheManager && -1 === e.indexOf("?v=") && this.cacheManager.addFile(e, t), i(t)
          } else i(null, t.getErrorMsg(s))
        },
        fail: e => i(null, t.getErrorMsg(e))
      });
      s && r && r.onProgressUpdate && r.onProgressUpdate(e => {
        s(e.progress)
      })
    }
    readFile(e, s, i) {
      let r = this.urlToFilePath(e);
      t.PAL.fs.readFile(r, "arraybuffer" === s ? null : "utf8").then(e => {
        switch (s) {
          case "json":
            i(JSON.parse(e));
            break;
          case "xml":
            i(new t.XML(e));
            break;
          default:
            i(e)
        }
      }).catch(e => i(null, t.getErrorMsg(e)))
    }
    urlToFilePath(e) {
      let t = e.lastIndexOf("?");
      return -1 != t ? e.substring(0, t) : e
    }
    checkSubpackagePrefix(e) {
      if (!this.supportSubPackageMultiLevelFolders && this.subPackages)
        for (let t in this.subPackages)
          if (e.startsWith(t)) {
            e = this.subPackages[t] + e.substring(t.length);
            break
          } return e
    }
    escapeURL(e) {
      if (!this.escapeZhCharsInURL) return e;
      let t = "",
        s = e.length;
      for (let i = 0; i < s; i++) {
        let s = e[i];
        if (r.test(s)) t += s;
        else try {
          t += encodeURI(s)
        } catch (e) {
          console.warn("errorInfo", ">>>" + s)
        }
      }
      return t
    }
  }
  const r = new RegExp("[-_.!~*'();/?:@&=+$,#%]|[0-9|A-Z|a-z]");
  class a {
    open(e, s) {
      let i = !1;
      this.ws = t.PAL.g.connectSocket(Object.assign({
        url: e,
        multiple: !0,
        fail: e => {
          i = !0, this.onError(e)
        }
      }, s)), null == this.ws || i ? this.ws = null : (this.ws.onOpen(e => this.onOpen(e)), this.ws.onClose(() => this.onClose()), this.ws.onError(e => this.onError(e)), this.ws.onMessage(e => {
        if (e.data) {
          var t = e.data;
          t.isBuffer && (t = this.base64ToArrayBuffer(t.data)), this.onMessage(t)
        }
      }))
    }
    base64ToArrayBuffer(e) {
      const t = atob(e),
        s = t.length,
        i = new Uint8Array(s);
      for (let e = 0; e < s; e++) i[e] = t.charCodeAt(e);
      return i.buffer
    }
    close() {
      this.ws && this.ws.close({})
    }
    send(e) {
      return null == this.ws ? Promise.reject("WebSocket is not open") : new Promise((t, s) => {
        this.ws.send({
          data: e,
          success: () => t(),
          fail: e => s(e)
        })
      })
    }
  }
  class o extends t.BrowserAdapter {
    constructor() {
      super(...arguments), this._visible = !0, this._orientation = "portrait-primary"
    }
    init() {
      var e;
      console.time || (console.time = function(e) {}, console.timeEnd = function(e) {
        console.log(e)
      }), t.Browser.isDomSupported = !1, null === (e = o.beforeInit) || void 0 === e || e.call(o), this._supportSetCursor = t.PAL.hasAPI("setCursor"), this._supportCreateArrayBufferURL = t.PAL.hasAPI("createBufferURL"), t.PAL.hasAPI("connectSocket") ? this.webSocketClass = a : this.webSocketClass = null;
      let s = "",
        i = t.PAL.hasAPI("getSystemInfoSync") ? t.PAL.g.getSystemInfoSync() : null;
      if (i) this._pixelRatio = i.pixelRatio, this._orientation = "landscape" === i.deviceOrientation ? "landscape-primary" : "portrait-primary", s = i.platform || "";
      else if (t.PAL.hasAPI("getWindowInfo")) {
        let e = t.PAL.g.getWindowInfo();
        if (this._pixelRatio = e.pixelRatio, t.PAL.g.getDeviceInfo) {
          s = t.PAL.g.getDeviceInfo().platform || ""
        }
      }(t.Browser.onVVMiniGame || t.Browser.onQGMiniGame) && (this._pixelRatio = window.devicePixelRatio), this.setPlatform("", s), i = i || {};
      const {
        SDKVersion: r
      } = t.PAL.hasAPI("getAppBaseInfo") ? t.PAL.g.getAppBaseInfo() : i;
      t.Browser.SDKVersion = r || "";
      const {
        system: n
      } = t.PAL.hasAPI("getDeviceInfo") ? t.PAL.g.getDeviceInfo() : i, l = n ? n.split(" ") : [];
      t.Browser.systemVersion = l.length ? l[l.length - 1] : "", t.Browser.onHWMiniGame ? this._pixelRatio = 1 : 1 === this._pixelRatio && t.Browser.onPC && !t.Browser.onDevTools && (this._pixelRatio = 2), t.PAL.g.onShow(() => {
        this._visible = !0, this.event(t.Event.VISIBILITY_CHANGE, !0), this.event(t.Event.FOCUS)
      }), t.PAL.g.onHide(() => {
        this._visible = !1, this.event(t.Event.VISIBILITY_CHANGE, !1), this.event(t.Event.BLUR)
      }), t.PAL.hasAPI("onWindowResize") && t.PAL.g.onWindowResize(e => {
        this.event(t.Event.RESIZE)
      })
    }
    start() {
      var e;
      let s = t.Loader.downloader = new i(t.PAL.hasAPI("getFileSystemManager") && t.PAL.hasAPI(t.PAL.g.getFileSystemManager(), "writeFile"));
      return this.setupWasmSupport(), null === (e = o.afterInit) || void 0 === e || e.call(o), s.cacheManager ? s.cacheManager.start() : Promise.resolve()
    }
    onInitRender() {
      if (t.Browser.onTBMiniGame && t.LayaGL.renderEngine._supportCapatable.turnOffSRGB(), t.Browser.onAlipayMiniGame && (t.LayaGL.renderEngine._supportCapatable.turnOffSRGB(), t.LayaGL.renderEngine._supportCapatable.turnOffCapableAndExtension(t.RenderCapable.MSAA, null)), t.Browser.onTBMiniGame && !t.PAL.g.isIDE) {
        let e = t.LayaGL.renderEngine._context;
        e.pixelStorei(e.UNPACK_PREMULTIPLY_ALPHA_WEBGL, !1)
      }
      t.Browser.onHWMiniGame && (t.LayaGL.textureContext.needBitmap = !1)
    }
    setupWasmSupport() {
      let e;
      if (t.Browser.onWXMiniGame ? e = window.WXWebAssembly : t.Browser.onAlipayMiniGame ? e = window.MYWebAssembly : t.Browser.onTTMiniGame ? e = window.TTWebAssembly : t.Browser.onHWMiniGame && (e = window.qg), e) window.WebAssembly || (window.WebAssembly = {
        Memory: e.Memory
      }), t.WasmAdapter.Memory = e.Memory, t.WasmAdapter.instantiateWasm = (s, i) => (s = t.WasmAdapter.locateFileDefault(s), e.instantiate(s, i));
      else if (window.WebAssembly) {
        let e = null != t.PAL.g.setWasmTaskCompile;
        t.WasmAdapter.instantiateWasm = (s, i) => (s = t.WasmAdapter.locateFileDefault(s), t.Laya.loader.fetch(s, "arraybuffer").then(r => r ? (e && (e = !1, t.PAL.g.setWasmTaskCompile(!0)), window.WebAssembly.instantiate(r, i)) : (console.error("WASM file not found: " + s), null)))
      }
    }
    getVisibility() {
      return this._visible
    }
    getScreenOrientation() {
      return this._orientation
    }
    createMainCanvas() {
      let e;
      return e = t.Browser.onTBMiniGame ? window.screencanvas || window.canvas.getRealCanvas() : window.canvas || window.__canvas, e.id = "layaCanvas", e
    }
    createElement(e) {
      var s;
      let i;
      return i = "canvas" === e && "function" == typeof t.PAL.g.createCanvas ? t.PAL.g.createCanvas() : super.createElement(e), i.style ? i.style === (null === (s = window.canvas) || void 0 === s ? void 0 : s.style) && (i.style = {}) : i.style = {}, i
    }
    getElementById(e) {
      return window.document.getElementById ? window.document.getElementById(e) : (t.PAL.warnIncompatibility("getElementById"), null)
    }
    removeElement(e) {
      e.remove ? e.remove() : e.dispose ? e.dispose() : e = null
    }
    setCursor(e) {
      if (!this._supportSetCursor) return;
      let s = e.split(" "),
        i = s[1] ? parseInt(s[1].trim()) : 0,
        r = s[2] ? parseInt(s[2].trim()) : 0,
        a = s[0].indexOf("url(");
      if (-1 != a) {
        let e = s[0].indexOf(")"); - 1 != e && (s[0] = s[0].substring(a + 4, e))
      }(isNaN(i) || isNaN(r)) && (i = r = void 0), t.PAL.g.setCursor(s[0], i, r)
    }
    get supportArrayBufferURL() {
      return this._supportCreateArrayBufferURL
    }
    createBufferURL(e) {
      return t.PAL.g.createBufferURL(e)
    }
    revokeBufferURL(e) {
      return t.PAL.g.revokeBufferURL(e)
    }
    getOpenDataContextCanvas() {
      return window.sharedCanvas
    }
    postMessageToOpenDataContext(e) {
      t.PAL.g.getOpenDataContext && t.PAL.g.getOpenDataContext().postMessage(e)
    }
    onCaptureGlobalError(e, s) {
      e ? (t.PAL.hasAPI("onError") && t.PAL.g.onError(s), t.PAL.g.onUnhandledRejection && t.PAL.g.onUnhandledRejection(s)) : (t.PAL.hasAPI("offError") && t.PAL.g.offError(s), t.PAL.g.offUnhandledRejection && t.PAL.g.offUnhandledRejection(s))
    }
    alert(e) {
      "function" == typeof window.alert ? window.alert.call(null, e) : console.warn("alert is not a function")
    }
  }
  t.PAL.register("browser", o);
  class n extends t.DeviceAdapter {
    constructor() {
      super(), this._watchId = 1, this._watchDic = new Map, this._accInfo = {
        x: 0,
        y: 0,
        z: 0
      }, this._rotInfo = {
        alpha: 0,
        beta: 0,
        gamma: 0,
        absolute: !1,
        compassAccuracy: 0
      }
    }
    get supportedLocation() {
      return !(!t.PAL.g.getFuzzyLocation && !t.PAL.g.getLocation)
    }
    getCurrentPosition(e, s, i) {
      t.PAL.g.getFuzzyLocation ? t.PAL.g.getFuzzyLocation({
        type: "gcj02",
        success: t => {
          e({
            latitude: t.latitude,
            longitude: t.longitude,
            timestamp: performance.now()
          })
        },
        fail: e => {
          null == s || s({
            code: 1,
            message: e.errMsg
          })
        }
      }) : t.PAL.g.getLocation({
        type: "gcj02",
        success: t => {
          e({
            latitude: t.latitude,
            longitude: t.longitude,
            speed: t.speed,
            altitude: t.altitude,
            accuracy: t.accuracy,
            timestamp: performance.now()
          })
        },
        fail: e => {
          null == s || s({
            code: 1,
            message: e.errMsg
          })
        }
      })
    }
    watchPosition(e, s, i) {
      return 0 === this._watchDic.size && (t.ILaya.systemTimer.loop(1e3, this, this.onUpdate), this._watchOptions = i), this._watchId++, this._watchDic.set(this._watchId, {
        successCallback: e,
        errorCallback: s
      }), this._watchId
    }
    clearWatchPosition(e) {
      this._watchDic.delete(e), 0 === this._watchDic.size && (t.ILaya.systemTimer.clear(this, this.onUpdate), this._watchOptions = null)
    }
    onUpdate() {
      let e = Array.from(this._watchDic.values());
      this.getCurrentPosition(t => e.forEach(e => e.successCallback(t)), t => e.forEach(e => {
        var s;
        return null === (s = e.errorCallback) || void 0 === s ? void 0 : s.call(e, t)
      }), this._watchOptions)
    }
    startListeningDeviceMotion() {
      t.PAL.g.startAccelerometer({
        interval: "game"
      }), t.PAL.g.onAccelerometerChange(e => {
        Object.assign(this._accInfo, e), this.event("devicemotion", [this._accInfo, this._accInfo, {}, 0])
      })
    }
    startListeningDeviceOrientation() {
      t.PAL.g.startGyroscope({
        interval: "game"
      }), t.PAL.g.onGyroscopeChange(e => {
        this._rotInfo.alpha = e.z, this._rotInfo.beta = e.x, this._rotInfo.gamma = e.y, this.event("deviceorientation", [!0, this._rotInfo])
      })
    }
  }
  t.PAL.register("device", n);
  class l extends t.FileSystemAdapter {
    constructor() {
      super(), this.hasAccess = !1, this.fs = t.PAL.g.getFileSystemManager(), this.hasAccess = t.PAL.hasAPI(this.fs, "access")
    }
    readFile(e, t) {
      return new Promise((s, i) => {
        var r;
        this.fs.readFile({
          filePath: e,
          encoding: null !== (r = t) && void 0 !== r ? r : void 0,
          success: e => s(e.data),
          fail: e => i(e)
        })
      })
    }
    writeFile(e, t, s) {
      return new Promise((i, r) => {
        var a;
        this.fs.writeFile({
          filePath: e,
          data: t,
          encoding: null !== (a = s) && void 0 !== a ? a : void 0,
          success: () => i(),
          fail: e => r(e)
        })
      })
    }
    unlink(e) {
      return new Promise((t, s) => {
        this.fs.unlink({
          filePath: e,
          success: () => t(),
          fail: e => s(e)
        })
      })
    }
    copyFile(e, t) {
      return new Promise((s, i) => {
        this.fs.copyFile({
          srcPath: e,
          destPath: t,
          success: () => s(),
          fail: e => i(e)
        })
      })
    }
    exists(e) {
      return new Promise(t => {
        this.hasAccess ? this.fs.access({
          path: e,
          success: () => t(!0),
          fail: e => t(!1)
        }) : this.fs.getFileInfo({
          filePath: e,
          success: e => t(!0),
          fail: e => t(!1)
        })
      })
    }
    getFileSize(e) {
      return new Promise((t, s) => {
        this.fs.getFileInfo({
          filePath: e,
          success: e => t(e.size),
          fail: e => s(e)
        })
      })
    }
    mkdir(e, t) {
      return new Promise((s, i) => {
        this.fs.mkdir({
          dirPath: e,
          recursive: null == t ? void 0 : t.recursive,
          success: () => s(),
          fail: e => i(e)
        })
      })
    }
    rmdir(e, t) {
      try {
        return this.fs.rmdirSync(e, null == t ? void 0 : t.recursive), Promise.resolve()
      } catch (e) {
        return Promise.reject(e)
      }
    }
    readdir(e) {
      return new Promise((t, s) => {
        this.fs.readdir({
          dirPath: e,
          success: e => t(e.files),
          fail: e => s(e)
        })
      })
    }
    unzip(e, t) {
      return new Promise((s, i) => {
        this.fs.unzip({
          zipFilePath: e,
          targetPath: t,
          success: () => s(),
          fail: e => i(e)
        })
      })
    }
  }
  t.PAL.register("fs", l);
  class h extends t.FontAdapter {
    loadFont(e) {
      return t.PAL.g.loadFont ? e.loader.fetch(e.url, "filePath", e.progress.createCallback(), e.options).then(e => {
        if (e) {
          let s = t.PAL.g.loadFont(e);
          return s ? {
            family: s
          } : null
        }
        return null
      }) : (t.PAL.warnIncompatibility("TTFont"), Promise.resolve(null))
    }
  }
  t.PAL.register("font", h);
  class c extends t.SoundChannel {
    get position() {
      return this._ctx ? this._ctx.currentTime : 0
    }
    get duration() {
      return this._ctx ? this._ctx.duration : 0
    }
    onPlay(e) {
      t.Laya.loader.fetch(e, "filePath").then(e => this.onLoaded(e))
    }
    onLoaded(e) {
      if (!this._started || null == e) return;
      this._loaded = !0;
      let s = this._ctx = this.createContext();
      s.onError(e => {
        console.error("MgInnerAudioChannel: " + t.getErrorMsg(e)), this.stop()
      }), s.onEnded(() => this.onPlayEnd());
      let i = () => {
        this._ctx && !this._paused && (0 != this.startTime && s.seek(this.startTime), s.play()), s.offCanplay(i)
      };
      s.onCanplay(i), s.src = e, s.playbackRate = this.playbackRate, s.loop = 0 === this.loops, s.volume = this._muted ? 0 : this._volume
    }
    onPlayAgain() {
      0 != this.startTime && this._ctx.seek(this.startTime), this._ctx.play()
    }
    onStop() {
      this.releaseContext()
    }
    onPause() {
      this._ctx.pause()
    }
    onResume() {
      this._ctx.play()
    }
    onVolumeChanged() {
      this._ctx.volume = this._muted ? 0 : this._volume
    }
    onMuted() {
      this._muted ? this._ctx.pause() : this._ctx.play()
    }
    createContext() {
      return t.PAL.g.createInnerAudioContext()
    }
    releaseContext() {
      var e;
      null === (e = this._ctx) || void 0 === e || e.destroy(), this._ctx = null
    }
  }
  class d extends t.VideoPlayerBackend {
    constructor() {
      super(...arguments), this._loop = !1, this._ended = !1, this._muted = !1, this._playbackRate = 1
    }
    get loop() {
      return this._loop
    }
    set loop(e) {
      this._loop = e, this.video && (this.video.loop = e)
    }
    get ended() {
      return this._ended
    }
    get currentTime() {
      return this._currentTime
    }
    set currentTime(e) {
      this.video && this.video.seek(1e3 * e)
    }
    get muted() {
      return this._muted
    }
    set muted(e) {
      this._muted = e, this.video && (this.video.muted = e)
    }
    get playbackRate() {
      return this._playbackRate
    }
    set playbackRate(e) {
      this._playbackRate = e, this.video && (this.video.playbackRate = e)
    }
    onLoad(e) {
      this._ended = !1, this._loaded && this.video.destroy(), this.video = t.PAL.g.createVideo(Object.assign({}, this.options, this.getNodeTransform(), {
        src: t.URL.postFormatURL(t.URL.formatURL(e)),
        autoplay: this._playing,
        loop: this._loop,
        muted: this._muted,
        playbackRate: this._playbackRate
      })), this.video.onEnded(() => this._ended = !0), this.video.onError(e => {
        console.error("MgVideoPlayer: " + t.getErrorMsg(e))
      }), this.setLoaded()
    }
    onPlay() {
      this.video.play()
    }
    onPause() {
      this.video.pause()
    }
    onTransformChanged() {
      if (!this.video) return;
      let {
        x: e,
        y: s,
        width: i,
        height: r
      } = this.getNodeTransform();
      this.video.paintTo ? this.video.paintTo(t.Browser.mainCanvas.source, e, s, 0, 0, i, r) : (this.video.x = e, this.video.y = s, this.video.width = i, this.video.height = r)
    }
    onDestroy() {
      this.video.destroy()
    }
  }
  class u extends c {
    constructor(e) {
      super(e)
    }
    createContext() {
      return t.PAL.g.createInnerAudioContext({
        useWebAudioImplement: !0
      })
    }
  }
  class g extends t.MediaAdapter {
    init() {
      t.PAL.g.createWebAudioContext ? this.audioCtx = t.PAL.g.createWebAudioContext() : t.PAL.g.getAudioContext && (this.audioCtx = t.PAL.g.getAudioContext()), this.longAudioClass = c, this.shortAudioClass = u, this.videoPlayerClass = t.PAL.hasAPI("createVideo") ? d : null, this.videoTextureClass = null
    }
  }
  t.PAL.register("media", g);
  class p extends t.StorageAdapter {
    checkSupport() {
      return t.PAL.hasAPI("getStorageInfoSync")
    }
  }
  t.PAL.register("storage", p);
  class f extends t.TextInputAdapter {
    constructor() {
      super(), this._editInline = !1, f.enabled = t.PAL.hasAPI("onKeyboardInput"), f.enabled && (t.PAL.g.onKeyboardInput(this.onKeyboardInput.bind(this)), t.PAL.g.onKeyboardConfirm(this.onKeyboardConfirm.bind(this)), t.PAL.g.onKeyboardComplete(this.onKeyboardComplete.bind(this)))
    }
    setText(e) {
      f.enabled && t.PAL.g.updateKeyboard({
        value: e
      })
    }
    onBegin() {
      return Promise.resolve()
    }
    onCanShowKeyboard() {
      let e = this.target;
      return e.editable && f.enabled ? new Promise((s, i) => {
        t.PAL.g.showKeyboard({
          defaultValue: e.text,
          maxLength: e.maxChars <= 0 ? 1e5 : e.maxChars,
          multiple: e.multiline,
          confirmHold: !0,
          confirmType: e.confirmType,
          keyboardType: "text",
          success: s,
          fail: i
        })
      }) : Promise.resolve()
    }
    onEnd(e, s, i) {
      return s || i || !f.enabled ? Promise.resolve() : new Promise((e, s) => {
        t.PAL.g.hideKeyboard({
          success: e,
          fail: s
        })
      })
    }
    onKeyboardInput(e) {
      if (!this.target) return;
      let s = this.validateText(e.value);
      this.updateTargetText(s) && this.target.event(t.Event.INPUT)
    }
    onKeyboardConfirm(e) {
      this.target && (this.onKeyboardInput(e), this.target.event(t.Event.ENTER), this.end())
    }
    onKeyboardComplete(e) {
      this.end(!0)
    }
  }
  f.enabled = !0, t.PAL.register("textInput", f);
  class m extends t.VideoTexture {
    constructor() {
      super(), this._ended = !1, this._waitFirstFrame = !1, this.decoder = t.PAL.g.createVideoDecoder({
        type: "wemedia"
      }), this.decoder.on("frame", e => {
        this._currentTime = e.pts / 1e3, this._waitFirstFrame && (this._waitFirstFrame = !1, this._playing || (this.render(!0), this.decoder.wait(!0)))
      }), this.decoder.on("ended", () => {
        this._loop ? this.decoder.stop().then(() => this.decoder.start(this._startOption)) : (this._ended = !0, this.event("ended"))
      })
    }
    get readyState() {
      return this._loaded ? 1 : 0
    }
    get ended() {
      return this._ended
    }
    get currentTime() {
      return this._currentTime
    }
    set currentTime(e) {
      this.decoder.seek(1e3 * e)
    }
    onLoad(e) {
      let s = this._source;
      this._ended = !1, this._waitFirstFrame = !1, this._loaded && this.decoder.stop(), this._loaded = !1, this._source === s && (this._startOption = {}, this._startOption.source = t.URL.postFormatURL(t.URL.formatURL(e)), t.Browser.isIOSHighPerformanceModePlus && (this._startOption.videoDataType = 2), this.decoder.start(this._startOption).then(e => {
        this.setLoaded(e.width, e.height, !0), this._playing || (this._waitFirstFrame = !0)
      }).catch(e => {
        console.warn("MgVideoTexture: " + e.message)
      }))
    }
    onPlay() {
      this.decoder.wait(!1)
    }
    onPause() {
      this.decoder.wait(!0)
    }
    onStop() {
      this.decoder.stop()
    }
    onRender() {
      const e = this.decoder.getFrameData();
      if (e) {
        const {
          data: s
        } = e;
        return t.LayaGL.textureContext.setTexturePixelsData(this._texture, new Uint8ClampedArray(s), !1, !1), !0
      }
      return !1
    }
    onDestroy() {
      this.decoder.remove()
    }
  }
  o.beforeInit = function() {
    t.Browser.onWXMiniGame = !0, t.Browser.onMiniGame = !0, t.Browser.isIOSHighPerformanceMode = GameGlobal.isIOSHighPerformanceMode, t.Browser.isIOSHighPerformanceModePlus = GameGlobal.isIOSHighPerformanceModePlus, t.PAL.g = window.wx, t.PAL.g.willGenerateUndefinedAPIs = !0
  }, o.afterInit = function() {
    t.Browser.onDevTools || (t.PAL.media.videoTextureClass = m)
  }, e.MgBrowserAdapter = o, e.MgCacheManager = s, e.MgDeviceAdapter = n, e.MgDownloader = i, e.MgFileSystemAdapter = l, e.MgFontAdapter = h, e.MgInnerAudioChannel = c, e.MgMediaAdapter = g, e.MgStorageAdapter = p, e.MgTextInputAdapter = f, e.MgVideoPlayer = d, e.MgWebAudioChannel = u, e.MgWebSocket = a, e.WxVideoTexture = m
}(window.Laya = window.Laya || {}, Laya);