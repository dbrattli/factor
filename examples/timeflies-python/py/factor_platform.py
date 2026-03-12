"""Fable.Actor platform implementation for Python.

Cooperative single-threaded model for use with tkinter or asyncio.
All actors run on the main thread using CPS-based receive.
"""

import time
import heapq
import uuid


# ---------------------------------------------------------------------------
# Unique references
# ---------------------------------------------------------------------------

class Ref:
    __slots__ = ("_id",)

    def __init__(self):
        self._id = uuid.uuid4()

    def __eq__(self, other):
        return isinstance(other, Ref) and self._id == other._id

    def __hash__(self):
        return hash(self._id)

    def __repr__(self):
        return f"Ref({self._id.hex[:8]})"


# ---------------------------------------------------------------------------
# Process
# ---------------------------------------------------------------------------

_processes: dict[int, "Process"] = {}
_current_proc = None


class Process:
    _next_pid = 0

    def __init__(self):
        Process._next_pid += 1
        self.pid = Process._next_pid
        self.queue = []  # list of ("msg", payload) or ("timer", callback) or ("reply", ref, value)
        self.linked: list = []
        self.parent_pid = None
        self.trap_exits = False
        self.alive = True
        self.pending_recv = None  # cont function or None
        _processes[self.pid] = self


# ---------------------------------------------------------------------------
# Ready queue for deferred spawn
# ---------------------------------------------------------------------------

_ready_queue: list = []


def _flush_ready():
    while _ready_queue:
        task = _ready_queue.pop(0)
        task()


# ---------------------------------------------------------------------------
# Process lifecycle
# ---------------------------------------------------------------------------

def spawn(f):
    """Spawn a cooperative process. Defers body to ready queue."""
    proc = Process()

    def run_setup():
        global _current_proc
        saved = _current_proc
        _current_proc = proc
        try:
            f()
        except SystemExit:
            pass
        except Exception as e:
            _handle_crash(proc, e)
        finally:
            _current_proc = saved

    _ready_queue.append(run_setup)
    return proc.pid


def spawn_linked(f):
    """Spawn a linked cooperative process."""
    parent = _current_proc
    proc = Process()
    if parent:
        proc.parent_pid = parent.pid
        parent.linked.append(proc.pid)

    def run_setup():
        global _current_proc
        saved = _current_proc
        _current_proc = proc
        try:
            f()
        except SystemExit:
            pass
        except Exception as e:
            _handle_crash(proc, e)
        finally:
            _current_proc = saved

    _ready_queue.append(run_setup)
    return proc.pid


def _handle_crash(proc, error):
    import traceback
    print(f"[crash] pid={proc.pid}: {error}", flush=True)
    traceback.print_exc()
    proc.alive = False
    if proc.parent_pid and proc.parent_pid in _processes:
        parent = _processes[proc.parent_pid]
        if parent.trap_exits:
            # Deliver EXIT as a regular message
            _deliver(parent, ("msg", None, {"pid": proc.pid, "reason": str(error)}))


def self_pid():
    return _current_proc.pid if _current_proc else 0


def make_ref():
    return Ref()


def kill_process(pid):
    if pid in _processes:
        proc = _processes[pid]
        proc.alive = False
        for child_pid in proc.linked:
            kill_process(child_pid)


def exit_normal():
    raise SystemExit("normal")


def trap_exits():
    if _current_proc:
        _current_proc.trap_exits = True


def format_reason(reason):
    return str(reason)


# ---------------------------------------------------------------------------
# Message passing
# ---------------------------------------------------------------------------

def send_msg(pid, msg):
    """Send a message to an actor."""
    if pid in _processes:
        proc = _processes[pid]
        _deliver(proc, ("msg", None, msg))


def _deliver(proc, item):
    """Deliver a message to a process. If pending receive, call continuation."""
    if not proc.alive:
        return
    proc.queue.append(item)
    _try_dispatch(proc)


def _try_dispatch(proc):
    """Try to satisfy a pending receive."""
    if proc.pending_recv is None:
        return

    # Look for a "msg" item in the queue
    i = 0
    while i < len(proc.queue):
        tag, _, payload = proc.queue[i]
        if tag == "msg":
            proc.queue.pop(i)
            cont = proc.pending_recv
            proc.pending_recv = None
            _run_continuation(proc, cont, payload)
            return
        elif tag == "timer":
            proc.queue.pop(i)
            payload()
            continue
        else:
            i += 1


def receive(cont):
    """CPS receive — register continuation, return immediately.

    When a message arrives, the continuation is called.
    If messages are already queued, dispatches immediately.
    """
    proc = _current_proc

    # Trampoline: if called recursively from within a continuation
    if hasattr(proc, '_trampoline'):
        proc._trampoline = cont
        return

    # Check for queued messages
    for i, item in enumerate(proc.queue):
        tag, _, payload = item
        if tag == "msg":
            proc.queue.pop(i)
            _run_continuation(proc, cont, payload)
            return
        elif tag == "timer":
            proc.queue.pop(i)
            payload()
            # Restart scan
            return receive_(cont)

    # No message available — suspend
    proc.pending_recv = cont


def _run_continuation(proc, cont, payload):
    """Run a receive continuation with trampoline support."""
    global _current_proc
    saved = _current_proc
    _current_proc = proc

    try:
        proc._trampoline = None
        cont(payload)

        # Trampoline loop: if cont called receive_ again
        while proc._trampoline is not None:
            next_cont = proc._trampoline
            proc._trampoline = None

            # Try to find next message immediately
            found = False
            i = 0
            while i < len(proc.queue):
                tag, _, p = proc.queue[i]
                if tag == "msg":
                    proc.queue.pop(i)
                    proc._trampoline = None
                    cont = next_cont
                    cont(p)
                    found = True
                    break
                elif tag == "timer":
                    proc.queue.pop(i)
                    p()
                    continue
                else:
                    i += 1

            if not found:
                # No message — suspend
                proc.pending_recv = next_cont
                break
    finally:
        _current_proc = saved
        if hasattr(proc, '_trampoline'):
            del proc._trampoline


# ---------------------------------------------------------------------------
# Reply (for Actor.call)
# ---------------------------------------------------------------------------

_reply_boxes: dict = {}


def send_reply(pid, ref, value):
    if ref not in _reply_boxes:
        _reply_boxes[ref] = []
    _reply_boxes[ref].append(value)


def recv_reply(ref):
    """Blocking poll for a reply. Used by Actor.call."""
    while True:
        if ref in _reply_boxes and _reply_boxes[ref]:
            return _reply_boxes[ref].pop(0)
        # Process timers while waiting
        _fire_timers()
        _flush_ready()
        time.sleep(0.001)


def ref_equals(a, b):
    return a == b


# ---------------------------------------------------------------------------
# Monitoring (stubs)
# ---------------------------------------------------------------------------

def monitor_process(pid):
    return Ref()


def demonitor_process(ref):
    pass


# ---------------------------------------------------------------------------
# Timer scheduling — heap-based, no threads
# ---------------------------------------------------------------------------

_timer_heap = []
_timer_seq = 0
_cancelled_timers = set()


def timer_schedule(ms, callback):
    global _timer_seq
    proc = _current_proc
    pid = proc.pid if proc else 0
    _timer_seq += 1
    seq = _timer_seq
    fire_time = time.monotonic() + int(ms) / 1000.0
    heapq.heappush(_timer_heap, (fire_time, seq, pid, callback))
    return (seq,)


def timer_cancel(timer_obj):
    if timer_obj and isinstance(timer_obj, tuple) and len(timer_obj) >= 1:
        seq = timer_obj[0]
        if isinstance(seq, int):
            _cancelled_timers.add(seq)


def _fire_timers():
    now = time.monotonic()
    while _timer_heap and _timer_heap[0][0] <= now:
        fire_time, seq, pid, callback = heapq.heappop(_timer_heap)
        if seq in _cancelled_timers:
            _cancelled_timers.discard(seq)
            continue
        if pid in _processes and _processes[pid].alive:
            proc = _processes[pid]
            # Timer fires as a message so receive_ can pick it up
            _deliver(proc, ("timer", None, callback))


# ---------------------------------------------------------------------------
# Scheduler pump (called from tkinter loop)
# ---------------------------------------------------------------------------

def process_timers(timeout_ms):
    """Main scheduler entry point. Fire timers, flush spawns, dispatch messages."""
    _fire_timers()
    _flush_ready()

    # Dispatch any pending messages for all processes
    for proc in list(_processes.values()):
        if proc.alive and proc.pending_recv is not None:
            _try_dispatch(proc)


# ---------------------------------------------------------------------------
# Bootstrap
# ---------------------------------------------------------------------------

def ensure_main_process():
    global _current_proc
    if _current_proc is None:
        proc = Process()
        _current_proc = proc
    return _current_proc
