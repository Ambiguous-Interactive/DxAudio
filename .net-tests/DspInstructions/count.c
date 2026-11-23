#include <valgrind/callgrind.h>

void start_count(void) { CALLGRIND_START_INSTRUMENTATION; }
void stop_count(void) { CALLGRIND_STOP_INSTRUMENTATION; }
