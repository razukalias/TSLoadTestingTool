# HttpOnly_4x100Sample

This sample contains four HTTP-only testcases with 100 request rows each and four HTTP steps per testcase.

- **PassFlow**: successful HTTP responses with status assertions.
- **FailFlow**: HTTP 500 responses checked against expected status 200.
- **NoAssertionFlow**: successful HTTP responses with no response assertions.
- **CastErrorFlow**: HTML returned while configured as JSON, exercising response parsing/cast-style failures.

The sample uses public httpbin.org endpoints and one thread/one iteration by default. Running all rows executes 1,600 HTTP steps.
