# CHANGELOG

## 5.0.0
- feat!: make `ListV2` and `StatV2` return an error code for each entry instead of throwing exceptions.
    - callers of these methods must now check the new property `Error` on each stat entry
- feat!: use enum for device connection state instead of string
- feat: add documentation

## 4.0.0
- feat!: change `Push` method signature to match `Pull`

## 3.2.0
- feat: add ScreenCapture support

## 3.1.1
- fix: don't break connection when sync error occurs

## 3.1.0
- feat: throw useful exception when using ListV2/StatV2

## 3.0.0
- feat!: replace stat/list with v2 versions (obsolete old versions)
- fix!: return full path on both stat and list

## 2.1.0
- feat: add StatV2 support (List/Stat size overflows above 4 GiB)

## 2.0.0
- fix!: use uint for stat element size
- fix: more consistent usage of cancellation token
- feat: add `host:track-devices support`

## 1.1.0
- Added support for stdin redirection when running shell commands

## 1.0.0
- Initial release