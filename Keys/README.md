# Test signing key

`test-debug.keystore` signs the test APKs that CI publishes to the `builds` branch. It plays the role of
Android's standard debug key: it is shared on purpose (password `farmworld-test`, alias `farmworld`) so
that every CI build carries the same signature and a new APK installs over the old one.

Do not use it for Google Play. The Play upload key must be a new keystore kept outside git
(GitHub secret), and Play App Signing must be enabled.
