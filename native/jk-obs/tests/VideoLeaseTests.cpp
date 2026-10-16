#include "VideoLease.hpp"
#include <iostream>
int main() {
    jk::VideoLease window(123);
    if (!window.CanRender(true, true, true, 123)) return 1;
    if (window.CanRender(true, false, false, 0)) { std::cerr << "Closed original window was not sealed\n"; return 2; }
    // A replacement with identical title, PID and recycled handle cannot reopen the item.
    if (window.CanRender(true, true, true, 123)) return 3;
    jk::VideoLease rebound(123);
    if (rebound.CanRender(true, true, true, 456)) return 4;
    jk::VideoLease exited(123);
    if (exited.CanRender(false, true, true, 123)) return 5;
    jk::VideoLease monitor(0);
    if (!monitor.CanRender(true, true, true, 0)) return 6;
    if (monitor.CanRender(true, true, false, 0)) return 7;
    return 0;
}
