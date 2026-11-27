/*
    Web Audio shim for the WebGL drive lane (PLAN.md W-CI, spec map 01, item 4).
    The drive step injects this file before Unity's loader, and it records every
    Web Audio surface the player touches into `window.__audioReport`:

      - AudioContext creation, sample rate, and running state
      - `createBuffer` calls with the buffer RMS (the input side)
      - `AudioBufferSourceNode.start` calls with the scheduled time and buffer RMS
        (the output side), counting one underrun per late explicit schedule
      - script-processor output blocks, worklet modules, and worklet nodes

    Measured limitation: buffer-source and script-processor levels read the
    PCM Unity hands to Web Audio, not samples captured after the graph. Unity
    mixes voices (including per-voice volume) inside its WASM audio engine, so
    those levels reflect the rendered mix for Unity builds, but a hypothetical
    downstream mute this shim cannot see would read as audible. Destination
    capture needs graph-changing instrumentation and stays with the spec's
    later tolerance runs.

    It also watches the console for the smoke player's `dxaudio-web-smoke`
    markers, so the driver can read which cues played and whether the sequence
    finished from one JSON document over the DevTools protocol. The file stays a
    plain script without modules because it loads before Unity's generated
    loader script and must not depend on anything Unity provides.
*/
(function () {
    "use strict";
    var install = function () {
        if (window.__audioReport) {
            return;
        }
        var report = {
            userAgent: String(navigator.userAgent),
            started: 0,
            underruns: 0,
            rms: 0.0,
            contexts: [],
            events: [],
            droppedEvents: 0,
            processors: 0,
            worklets: 0,
            played: { dry: false, shaped: false },
            done: false,
            errors: [],
        };
        window.__audioReport = report;
        var eventLimit = 8192;

        function bufferRms(buffer) {
            var total = 0.0;
            var count = 0;
            for (var channel = 0; channel < buffer.numberOfChannels; ++channel) {
                var data = buffer.getChannelData(channel);
                for (var index = 0; index < data.length; ++index) {
                    var sample = data[index];
                    total += sample * sample;
                }
                count += data.length;
            }
            return count > 0 ? Math.sqrt(total / count) : 0.0;
        }

        function note(event) {
            if (report.events.length >= eventLimit) {
                report.droppedEvents += 1;
                return;
            }
            report.events.push(event);
        }

        function noteRms(value) {
            if (value > report.rms) {
                report.rms = value;
            }
        }

        function trackContext(kind, context) {
            var entry = { kind: kind, sampleRate: context.sampleRate, state: context.state };
            report.contexts.push(entry);
            context.onstatechange = function () {
                entry.state = context.state;
            };
        }

        if (window.AudioContext) {
            var NativeAudioContext = window.AudioContext;
            window.AudioContext = class extends NativeAudioContext {
                constructor() {
                    super(...arguments);
                    trackContext("live", this);
                }
            };
        }
        if (window.OfflineAudioContext) {
            var NativeOfflineAudioContext = window.OfflineAudioContext;
            window.OfflineAudioContext = class extends NativeOfflineAudioContext {
                constructor() {
                    super(...arguments);
                    trackContext("offline", this);
                }
            };
        }

        var base = window.BaseAudioContext ? BaseAudioContext.prototype : null;
        if (base && base.createBuffer) {
            var nativeCreateBuffer = base.createBuffer;
            base.createBuffer = function () {
                var buffer = nativeCreateBuffer.apply(this, arguments);
                var rms = bufferRms(buffer);
                note({
                    kind: "buffer",
                    rms: rms,
                    duration: buffer.duration,
                    channels: buffer.numberOfChannels,
                    sampleRate: buffer.sampleRate,
                });
                noteRms(rms);
                return buffer;
            };
        }

        var sourcePrototype = window.AudioBufferSourceNode ? AudioBufferSourceNode.prototype : null;
        if (sourcePrototype && sourcePrototype.start) {
            var nativeStart = sourcePrototype.start;
            sourcePrototype.start = function (when) {
                // Record only starts the native call accepted: a rejected
                // start never plays and must not enter the measurement.
                var result = nativeStart.apply(this, arguments);
                var context = this.context;
                var scheduled = typeof when === "number" ? when : context ? context.currentTime : 0.0;
                var rms = this.buffer ? bufferRms(this.buffer) : 0.0;
                note({
                    kind: "start",
                    when: scheduled,
                    rms: rms,
                    duration: this.buffer ? this.buffer.duration : 0.0,
                    channels: this.buffer ? this.buffer.numberOfChannels : 0,
                    sampleRate: this.buffer ? this.buffer.sampleRate : 0,
                });
                report.started += 1;
                noteRms(rms);
                // start(0) and start() both request immediate playback; only
                // an explicit time in the past is a late schedule.
                if (typeof when === "number" && when > 0 && context && when < context.currentTime - 0.005) {
                    report.underruns += 1;
                }
                return result;
            };
        }

        if (base && base.createScriptProcessor) {
            var nativeCreateScriptProcessor = base.createScriptProcessor;
            var nativeOnAudioProcess = window.ScriptProcessorNode ? Object.getOwnPropertyDescriptor(ScriptProcessorNode.prototype, "onaudioprocess") : null;
            base.createScriptProcessor = function () {
                var processor = nativeCreateScriptProcessor.apply(this, arguments);
                report.processors += 1;
                var handler = null;
                var listening = false;
                var listenOnce = function () {
                    if (listening) {
                        return;
                    }
                    listening = true;
                    // Registered after the player's own handler, so this
                    // measures the block the player filled, not silence.
                    processor.addEventListener("audioprocess", function (event) {
                        var buffer = event.outputBuffer;
                        if (!buffer) {
                            return;
                        }
                        var rms = bufferRms(buffer);
                        note({
                            kind: "process",
                            rms: rms,
                            duration: buffer.duration,
                            channels: buffer.numberOfChannels,
                        });
                        noteRms(rms);
                    });
                };
                Object.defineProperty(processor, "onaudioprocess", {
                    get: function () {
                        return handler;
                    },
                    set: function (value) {
                        handler = value;
                        if (nativeOnAudioProcess && nativeOnAudioProcess.set) {
                            nativeOnAudioProcess.set.call(processor, value);
                        }
                        queueMicrotask(listenOnce);
                    },
                });
                return processor;
            };
        }

        if (window.AudioWorklet && window.AudioWorklet.prototype && window.AudioWorklet.prototype.addModule) {
            var nativeAddModule = window.AudioWorklet.prototype.addModule;
            window.AudioWorklet.prototype.addModule = function () {
                note({ kind: "module", url: String(arguments[0] || "") });
                return nativeAddModule.apply(this, arguments);
            };
        }
        if (window.AudioWorkletNode) {
            var NativeAudioWorkletNode = window.AudioWorkletNode;
            window.AudioWorkletNode = class extends NativeAudioWorkletNode {
                constructor() {
                    super(...arguments);
                    report.worklets += 1;
                    note({ kind: "worklet", name: String(arguments[1] || "") });
                }
            };
        }

        var watchConsole = function (method) {
            if (!console[method]) {
                return false;
            }
            var native = console[method].bind(console);
            console[method] = function () {
                var text = "";
                for (var index = 0; index < arguments.length; ++index) {
                    text += String(arguments[index]) + " ";
                }
                if (text.indexOf("dxaudio-web-smoke") >= 0) {
                    // The player logs ok:false when Play refuses a cue, so a
                    // marker alone must not count as played.
                    var ok = text.indexOf('"ok":true') >= 0;
                    if (text.indexOf('"cue":"dry"') >= 0) {
                        report.played.dry = ok;
                    }
                    if (text.indexOf('"cue":"shaped"') >= 0) {
                        report.played.shaped = ok;
                    }
                    if (text.indexOf('"event":"done"') >= 0) {
                        report.done = true;
                    }
                    note({ kind: "marker", text: text.slice(0, 400) });
                }
                return native.apply(console, arguments);
            };
            return console[method] !== native;
        };
        report.consoleWrapped = watchConsole("log");
        watchConsole("info");

        window.addEventListener("error", function (event) {
            if (report.errors.length < 32) {
                report.errors.push(String(event.message || "unknown error").slice(0, 400));
            }
        });
        report.ready = true;
    };
    try {
        install();
    } catch (error) {
        // The wraps installed before the throw keep working; record the gap.
        if (window.__audioReport && window.__audioReport.errors.length < 32) {
            window.__audioReport.errors.push("shim install: " + String(error).slice(0, 400));
        }
    }
})();
