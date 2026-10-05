/* Portable implementation of the original AlsaRecorder JNI contract.
 * Captures actual PCM through Android's audio input backend; never synthesizes it.
 */
#include <jni.h>
#include <errno.h>
#include <limits.h>
#include <pthread.h>
#include <stdint.h>
#include <stdlib.h>
#include <android/log.h>

typedef struct Recorder {
    jlong id;
    jobject object;
    jint unit;
    struct Recorder *next;
} Recorder;
static Recorder *recorders;
static jlong next_id = 1;
static pthread_mutex_t lock = PTHREAD_MUTEX_INITIALIZER;

static int exception(JNIEnv *env) {
    if (!(*env)->ExceptionCheck(env)) return 0;
    (*env)->ExceptionDescribe(env);
    (*env)->ExceptionClear(env);
    return 1;
}
static jobject lookup(JNIEnv *env, jlong id, jint *unit) {
    jobject result = NULL;
    pthread_mutex_lock(&lock);
    for (Recorder *r = recorders; r; r = r->next) {
        if (r->id == id) {
            result = (*env)->NewLocalRef(env, r->object);
            if (unit) *unit = r->unit;
            break;
        }
    }
    pthread_mutex_unlock(&lock);
    return result;
}
static int call_void(JNIEnv *env, jobject record, const char *method) {
    jclass type = (*env)->GetObjectClass(env, record);
    jmethodID fn = (*env)->GetMethodID(env, type, method, "()V");
    if (exception(env) || !fn) return -ENOSYS;
    (*env)->CallVoidMethod(env, record, fn);
    return exception(env) ? -EIO : 0;
}

JNIEXPORT jlong JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_create(
        JNIEnv *env, jclass owner, jint card, jint device, jint period, jint count,
        jint channels, jint rate, jint bits) {
    if ((channels != 1 && channels != 2) || (bits != 8 && bits != 16) ||
            rate <= 0 || period <= 0 || count <= 0) return 0;
    int64_t unit = (int64_t)period * channels * (bits / 8);
    if (unit > INT_MAX) return 0;
    int64_t requested = unit * count;
    if (requested > 20 * 1024 * 1024) return 0;
    jclass type = (*env)->FindClass(env, "android/media/AudioRecord");
    if (exception(env) || !type) return 0;
    jmethodID minimum = (*env)->GetStaticMethodID(env, type, "getMinBufferSize", "(III)I");
    if (exception(env) || !minimum) return 0;
    jmethodID constructor = (*env)->GetMethodID(env, type, "<init>", "(IIIII)V");
    if (exception(env) || !constructor) return 0;
    jmethodID state = (*env)->GetMethodID(env, type, "getState", "()I");
    if (exception(env) || !state) return 0;
    jint channel_config = channels == 1 ? 16 : 12;
    jint format = bits == 16 ? 2 : 3;
    jint buffer = (*env)->CallStaticIntMethod(env, type, minimum, rate, channel_config, format);
    if (exception(env) || buffer <= 0) return 0;
    if (requested > buffer) buffer = (jint)requested;
    jobject record = (*env)->NewObject(env, type, constructor, 1, rate, channel_config, format, buffer);
    if (exception(env) || !record) return 0;
    jint initialized = (*env)->CallIntMethod(env, record, state);
    if (exception(env) || initialized != 1) { call_void(env, record, "release"); return 0; }
    Recorder *r = calloc(1, sizeof(*r));
    if (!r) { call_void(env, record, "release"); return 0; }
    r->object = (*env)->NewGlobalRef(env, record);
    if (!r->object) { exception(env); free(r); call_void(env, record, "release"); return 0; }
    r->unit = (jint)unit;
    pthread_mutex_lock(&lock);
    r->id = next_id++;
    r->next = recorders; recorders = r;
    pthread_mutex_unlock(&lock);
    __android_log_print(ANDROID_LOG_INFO, "VietKEMU", "ALSA %d:%d mapped to actual Android microphone, %d Hz/%d channels/%d bits", card, device, rate, channels, bits);
    return r->id;
}
JNIEXPORT jint JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_getReadUnitSize(JNIEnv *env, jclass owner, jlong id) {
    jint unit = -EBADF;
    jobject record = lookup(env, id, &unit);
    if (record) (*env)->DeleteLocalRef(env, record);
    return unit;
}
JNIEXPORT jint JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_start(JNIEnv *env, jclass owner, jlong id) {
    jobject record = lookup(env, id, NULL);
    if (!record) return -EBADF;
    int result = call_void(env, record, "startRecording");
    (*env)->DeleteLocalRef(env, record);
    return result;
}
JNIEXPORT jint JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_read(JNIEnv *env, jclass owner, jlong id, jbyteArray data) {
    jint unit;
    jobject record = lookup(env, id, &unit);
    if (!record) return -EBADF;
    if (!data || (*env)->GetArrayLength(env, data) < unit) { (*env)->DeleteLocalRef(env, record); return -EINVAL; }
    jclass type = (*env)->GetObjectClass(env, record);
    jmethodID read = (*env)->GetMethodID(env, type, "read", "([BII)I");
    jint result = -ENOSYS;
    if (!exception(env) && read) {
        result = (*env)->CallIntMethod(env, record, read, data, 0, unit);
        if (exception(env)) result = -EIO;
    }
    (*env)->DeleteLocalRef(env, record);
    return result;
}
JNIEXPORT void JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_stop(JNIEnv *env, jclass owner, jlong id) {
    jobject record = lookup(env, id, NULL);
    if (record) { call_void(env, record, "stop"); (*env)->DeleteLocalRef(env, record); }
}
JNIEXPORT void JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_destroy(JNIEnv *env, jclass owner, jlong id) {
    Recorder *removed = NULL;
    pthread_mutex_lock(&lock);
    for (Recorder **slot = &recorders; *slot; slot = &(*slot)->next) {
        if ((*slot)->id == id) { removed = *slot; *slot = removed->next; break; }
    }
    pthread_mutex_unlock(&lock);
    if (removed) {
        call_void(env, removed->object, "stop");
        call_void(env, removed->object, "release");
        (*env)->DeleteGlobalRef(env, removed->object);
        free(removed);
    }
}
