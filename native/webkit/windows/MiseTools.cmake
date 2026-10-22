# vcpkg normally downloads private copies of generators. Pin these to the
# already selected mise executables instead, including host-only dependencies.
set(VCPKG_ENV_PASSTHROUGH DEVDUO_WEBKIT_SOURCE DEVDUO_WEBKIT_PYTHON
    DEVDUO_WEBKIT_RUBY DEVDUO_WEBKIT_PERL DEVDUO_WEBKIT_GPERF)
foreach(tool PYTHON RUBY PERL GPERF)
    if(NOT DEFINED ENV{DEVDUO_WEBKIT_${tool}} OR NOT EXISTS "$ENV{DEVDUO_WEBKIT_${tool}}")
        message(FATAL_ERROR "Missing mise-selected ${tool}; use BuildRuntime.ps1 through the optional mise environment")
    endif()
endforeach()
set(PYTHON3 "$ENV{DEVDUO_WEBKIT_PYTHON}")
set(RUBY "$ENV{DEVDUO_WEBKIT_RUBY}")
set(PERL "$ENV{DEVDUO_WEBKIT_PERL}")
set(GPERF "$ENV{DEVDUO_WEBKIT_GPERF}")
