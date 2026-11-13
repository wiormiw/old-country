# kotlinx.serialization keeps what it needs through its own rules (bundled in the library).

# SignalR's Java client reads hub messages with Gson (reflection), so the message classes it
# builds and the payload it fills in must keep their names.
-keep class com.microsoft.signalr.** { *; }
-keep class dev.wiormiw.oldcountry.live.ChangesMessage { *; }
# Optional dependencies of the client that Android doesn't have or need.
-dontwarn org.slf4j.**
-dontwarn javax.annotation.**
-dontwarn org.conscrypt.**
-dontwarn org.bouncycastle.**
-dontwarn org.openjsse.**
