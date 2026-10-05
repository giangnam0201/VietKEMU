/* Initial userspace compatibility adapter. Unsupported hardware operations return
 * explicit errors rather than reporting fake playback/recording success. */
#include <jni.h>
#include <errno.h>
#include <stdlib.h>
#include <sys/wait.h>
#include <android/log.h>
#define LOG(message) __android_log_print(ANDROID_LOG_WARN, "VietKEMU", "%s", message)
JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM *vm, void *reserved) { return JNI_VERSION_1_6; }
JNIEXPORT jint JNICALL Java_com_evideostb_vdk_os_RootShell_native_1execmd(JNIEnv *env, jclass cls, jstring input) {
    if (!input) return -EINVAL;
    const char *command = (*env)->GetStringUTFChars(env, input, NULL);
    if (!command) return -ENOMEM;
    int status = system(command);
    (*env)->ReleaseStringUTFChars(env, input, command);
    return status == -1 ? -errno : (WIFEXITED(status) ? WEXITSTATUS(status) : -EINTR);
}
JNIEXPORT jint JNICALL Java_com_evideostb_vdk_os_RootShell_native_1execmd_1interactively_1start(JNIEnv *e, jclass c, jstring s) { LOG("Interactive root daemon not implemented"); return -ENOSYS; }
JNIEXPORT jint JNICALL Java_com_evideostb_vdk_os_RootShell_native_1execmd_1interactively_1stop(JNIEnv *e, jclass c, jint fd) { return -ENOSYS; }
JNIEXPORT jint JNICALL Java_com_evideostb_vdk_hardware_VGAManager_setVgaOnOff(JNIEnv *e, jclass c, jint enabled) { LOG("VGA routing pending virtual-display adapter"); return -ENOSYS; }
JNIEXPORT jlong JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_create(JNIEnv *e, jclass c, jint a, jint b, jint d, jint f, jint g, jint h, jint i) { LOG("Microphone recording backend unavailable"); return 0; }
JNIEXPORT void JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_destroy(JNIEnv *e, jclass c, jlong p) {}
JNIEXPORT jint JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_getReadUnitSize(JNIEnv *e, jclass c, jlong p) { return -ENOSYS; }
JNIEXPORT jint JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_read(JNIEnv *e, jclass c, jlong p, jbyteArray b) { return -ENOSYS; }
JNIEXPORT jint JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_start(JNIEnv *e, jclass c, jlong p) { return -ENOSYS; }
JNIEXPORT void JNICALL Java_com_evideostb_vdk_media_AlsaRecorder_stop(JNIEnv *e, jclass c, jlong p) {}
