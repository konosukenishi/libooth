# Match Rust's +crt-static. With CMP0091=NEW, compiler flags alone do not
# override CMake's default /MD runtime selection for SDL's native targets.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
    CACHE STRING "Use the static MSVC runtime for native dependencies" FORCE)
