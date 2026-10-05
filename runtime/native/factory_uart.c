/* POSIX implementation of the original Serial JNI interface.
 * Missing device nodes stay unavailable; no factory-handshake data is invented.
 */
#include <jni.h>
#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <termios.h>
#include <unistd.h>

static int descriptor(JNIEnv *env, jobject self) {
    jclass serial = (*env)->GetObjectClass(env, self);
    jfieldID field = (*env)->GetFieldID(env, serial, "mTtyFd", "Ljava/io/FileDescriptor;");
    if (!field) return -1;
    jobject value = (*env)->GetObjectField(env, self, field);
    if (!value) return -1;
    jclass type = (*env)->GetObjectClass(env, value);
    jfieldID fd = (*env)->GetFieldID(env, type, "descriptor", "I");
    return fd ? (*env)->GetIntField(env, value, fd) : -1;
}

JNIEXPORT jobject JNICALL Java_com_example_jni_Serial_serialOpen(JNIEnv *env, jobject self, jstring path) {
    if (!path) return NULL;
    const char *name = (*env)->GetStringUTFChars(env, path, NULL);
    if (!name) return NULL;
    int fd = open(name, O_RDWR | O_NOCTTY | O_NONBLOCK | O_CLOEXEC);
    (*env)->ReleaseStringUTFChars(env, path, name);
    if (fd < 0) return NULL;
    jclass type = (*env)->FindClass(env, "java/io/FileDescriptor");
    jmethodID constructor = type ? (*env)->GetMethodID(env, type, "<init>", "()V") : NULL;
    jfieldID field = type ? (*env)->GetFieldID(env, type, "descriptor", "I") : NULL;
    if (!constructor || !field) { close(fd); return NULL; }
    jobject result = (*env)->NewObject(env, type, constructor);
    if (!result) { close(fd); return NULL; }
    (*env)->SetIntField(env, result, field, fd);
    return result;
}

JNIEXPORT jint JNICALL Java_com_example_jni_Serial_serialClose(JNIEnv *env, jobject self) {
    int fd = descriptor(env, self);
    if (fd < 0) return -EBADF;
    int result = close(fd);
    jclass type = (*env)->GetObjectClass(env, self);
    jfieldID field = (*env)->GetFieldID(env, type, "mTtyFd", "Ljava/io/FileDescriptor;");
    (*env)->SetObjectField(env, self, field, NULL);
    return result < 0 ? -errno : 0;
}

JNIEXPORT jint JNICALL Java_com_example_jni_Serial_serialSelect(JNIEnv *env, jobject self, jint timeout) {
    int fd = descriptor(env, self);
    if (fd < 0) return -EBADF;
    struct pollfd item = {.fd = fd, .events = POLLIN};
    int result = poll(&item, 1, timeout);
    return result < 0 ? -errno : result;
}

JNIEXPORT jint JNICALL Java_com_example_jni_Serial_serialSetParam(JNIEnv *env, jobject self, jint baud, jint bits, jint stops, jint parity) {
    int fd = descriptor(env, self);
    if (fd < 0) return -EBADF;
    speed_t speed;
    switch (baud) {
        case 1200: speed = B1200; break;
        case 2400: speed = B2400; break;
        case 4800: speed = B4800; break;
        case 9600: speed = B9600; break;
        case 19200: speed = B19200; break;
        case 38400: speed = B38400; break;
        case 57600: speed = B57600; break;
        case 115200: speed = B115200; break;
        default: return -EINVAL;
    }
    struct termios options;
    if (tcgetattr(fd, &options) < 0) return -errno;
    cfmakeraw(&options);
    options.c_cflag &= ~(CSIZE | CSTOPB | PARENB | PARODD);
    switch (bits) {
        case 5: options.c_cflag |= CS5; break;
        case 6: options.c_cflag |= CS6; break;
        case 7: options.c_cflag |= CS7; break;
        case 8: options.c_cflag |= CS8; break;
        default: return -EINVAL;
    }
    if (stops != 1 && stops != 2) return -EINVAL;
    if (stops == 2) options.c_cflag |= CSTOPB;
    if (parity < 0 || parity > 2) return -EINVAL;
    if (parity) options.c_cflag |= PARENB;
    if (parity == 1) options.c_cflag |= PARODD;
    options.c_cflag |= CLOCAL | CREAD;
    if (cfsetispeed(&options, speed) < 0 || cfsetospeed(&options, speed) < 0 ||
        tcsetattr(fd, TCSANOW, &options) < 0) return -errno;
    return 0;
}

static jint transfer(JNIEnv *env, jobject self, jbyteArray data, jint offset, jint length, int writing) {
    int fd = descriptor(env, self);
    if (fd < 0) return -EBADF;
    if (!data) return -EINVAL;
    jsize size = (*env)->GetArrayLength(env, data);
    if (offset < 0 || length < 0 || offset > size || length > size - offset) return -EINVAL;
    jbyte *bytes = (*env)->GetByteArrayElements(env, data, NULL);
    if (!bytes) return -ENOMEM;
    ssize_t result = writing ? write(fd, bytes + offset, length) : read(fd, bytes + offset, length);
    int error = errno;
    (*env)->ReleaseByteArrayElements(env, data, bytes, writing ? JNI_ABORT : 0);
    return result < 0 ? -error : (jint)result;
}
JNIEXPORT jint JNICALL Java_com_example_jni_Serial_serialReadData(JNIEnv *e, jobject s, jbyteArray b, jint o, jint n) { return transfer(e, s, b, o, n, 0); }
JNIEXPORT jint JNICALL Java_com_example_jni_Serial_serialWriteData(JNIEnv *e, jobject s, jbyteArray b, jint o, jint n) { return transfer(e, s, b, o, n, 1); }
