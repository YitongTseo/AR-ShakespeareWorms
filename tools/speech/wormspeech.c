// wormspeech: a thin C shim over sherpa-onnx's C API so the Unity side only marshals
// strings, ints and float buffers -- never sherpa's (large, version-sensitive) config structs.
// Built against the exact c-api.h of the bundled libsherpa-onnx-c-api.so by build_shim.sh.
#include "c-api.h"
#include <stdlib.h>
#include <string.h>

#define WS_EXPORT __attribute__((visibility("default")))

// ---------------------------------------------------------------- text to speech (VITS / piper)
WS_EXPORT void *ws_tts_create(const char *model, const char *tokens, const char *data_dir, int threads)
{
    SherpaOnnxOfflineTtsConfig c;
    memset(&c, 0, sizeof c);
    c.model.vits.model = model;
    c.model.vits.lexicon = "";
    c.model.vits.tokens = tokens;
    c.model.vits.data_dir = data_dir;
    c.model.vits.noise_scale = 0.667f;
    c.model.vits.noise_scale_w = 0.8f;
    c.model.vits.length_scale = 1.0f;
    c.model.num_threads = threads;
    c.model.debug = 0;
    c.model.provider = "cpu";
    c.max_num_sentences = 1;
    c.silence_scale = 0.2f;
    return (void *)SherpaOnnxCreateOfflineTts(&c);
}

WS_EXPORT int ws_tts_sample_rate(void *tts) { return SherpaOnnxOfflineTtsSampleRate((const SherpaOnnxOfflineTts *)tts); }
WS_EXPORT int ws_tts_num_speakers(void *tts) { return SherpaOnnxOfflineTtsNumSpeakers((const SherpaOnnxOfflineTts *)tts); }

// Returns an audio handle (free with ws_tts_free_audio) and points *samples at its buffer.
WS_EXPORT void *ws_tts_generate(void *tts, const char *text, int sid, float speed,
                                const float **samples, int *n, int *sample_rate)
{
    const SherpaOnnxGeneratedAudio *a = SherpaOnnxOfflineTtsGenerate((const SherpaOnnxOfflineTts *)tts, text, sid, speed);
    if (!a) { *samples = NULL; *n = 0; *sample_rate = 0; return NULL; }
    *samples = a->samples; *n = a->n; *sample_rate = a->sample_rate;
    return (void *)a;
}

WS_EXPORT void ws_tts_free_audio(void *audio) { SherpaOnnxDestroyOfflineTtsGeneratedAudio((const SherpaOnnxGeneratedAudio *)audio); }
WS_EXPORT void ws_tts_destroy(void *tts) { SherpaOnnxDestroyOfflineTts((const SherpaOnnxOfflineTts *)tts); }

// ---------------------------------------------------------------- speech to text (whisper)
WS_EXPORT void *ws_asr_create(const char *encoder, const char *decoder, const char *tokens, int threads)
{
    SherpaOnnxOfflineRecognizerConfig c;
    memset(&c, 0, sizeof c);
    c.feat_config.sample_rate = 16000;
    c.feat_config.feature_dim = 80;
    c.model_config.whisper.encoder = encoder;
    c.model_config.whisper.decoder = decoder;
    c.model_config.whisper.language = "en";
    c.model_config.whisper.task = "transcribe";
    c.model_config.whisper.tail_paddings = -1;
    c.model_config.tokens = tokens;
    c.model_config.num_threads = threads;
    c.model_config.debug = 0;
    c.model_config.provider = "cpu";
    c.model_config.model_type = "whisper";
    c.decoding_method = "greedy_search";
    c.max_active_paths = 4;
    return (void *)SherpaOnnxCreateOfflineRecognizer(&c);
}

// Returns a malloc'd UTF-8 string (free with ws_free). samples are mono floats in [-1, 1].
WS_EXPORT char *ws_asr_recognize(void *asr, const float *samples, int n, int sample_rate)
{
    const SherpaOnnxOfflineRecognizer *r = (const SherpaOnnxOfflineRecognizer *)asr;
    const SherpaOnnxOfflineStream *s = SherpaOnnxCreateOfflineStream(r);
    SherpaOnnxAcceptWaveformOffline(s, sample_rate, samples, n);
    SherpaOnnxDecodeOfflineStream(r, s);
    const SherpaOnnxOfflineRecognizerResult *res = SherpaOnnxGetOfflineStreamResult(s);
    char *out = strdup(res && res->text ? res->text : "");
    SherpaOnnxDestroyOfflineRecognizerResult(res);
    SherpaOnnxDestroyOfflineStream(s);
    return out;
}

WS_EXPORT void ws_asr_destroy(void *asr) { SherpaOnnxDestroyOfflineRecognizer((const SherpaOnnxOfflineRecognizer *)asr); }
WS_EXPORT void ws_free(void *p) { free(p); }
WS_EXPORT int ws_version(void) { return 1; }
