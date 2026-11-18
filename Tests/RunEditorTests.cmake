# Runs the editor's C# tests inside the editor in batch mode on a fresh copy of a project (cmake -P):
#   -DEDITOR=<HelixEditor> -DPROJECT=<folder> -DWORK=<folder the copy and the log go to> -DMETHOD=<Namespace.Class.Method>
#   [-DWINDOWED=1: with the editor's window (no batch mode), for the tests of its own windows]
#   [-DMETHOD2=<Namespace.Class.Method>: a second run on the same copy after the first (what the first left)]
# The tests save layouts into the project's UserSettings: the copy keeps the source tree clean. The editor must end with 0.
cmake_minimum_required(VERSION 3.28)

file(REMOVE_RECURSE "${WORK}")
file(MAKE_DIRECTORY "${WORK}")
file(COPY "${PROJECT}/" DESTINATION "${WORK}/Project")
set(log "${WORK}/Editor.log")
if(WINDOWED)
    set(mode "")
else()
    set(mode -batchmode)
endif()
execute_process(COMMAND "${EDITOR}" ${mode} -screen-width 960 -screen-height 540 -projectPath "${WORK}/Project" -executeMethod "${METHOD}"
                        -logFile "${log}"
                RESULT_VARIABLE code OUTPUT_VARIABLE output ERROR_VARIABLE errors TIMEOUT 300)
if(EXISTS "${log}")
    file(READ "${log}" text)
endif()
if(NOT code EQUAL 0)
    message(FATAL_ERROR "The editor's tests ended with ${code}:\n${output}\n${errors}\n${text}")
endif()
if(METHOD2)
    set(log2 "${WORK}/Editor2.log")
    execute_process(COMMAND "${EDITOR}" ${mode} -screen-width 960 -screen-height 540 -projectPath "${WORK}/Project" -executeMethod "${METHOD2}"
                            -logFile "${log2}"
                    RESULT_VARIABLE code2 OUTPUT_VARIABLE output2 ERROR_VARIABLE errors2 TIMEOUT 300)
    if(EXISTS "${log2}")
        file(READ "${log2}" text2)
    endif()
    if(NOT code2 EQUAL 0)
        message(FATAL_ERROR "The editor's second run ended with ${code2}:\n${output2}\n${errors2}\n${text2}")
    endif()
    string(APPEND text "${text2}")
endif()
string(REGEX MATCH "Editor tests: [^\n]*" summary "${text}")
message(STATUS "${summary}")
