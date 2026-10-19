"""Compare guest driving timelines with the host's authoritative timeline.

Usage: python Tools/analyze_coop_trace.py <host CoopDiagnostics/capture-id folder>
"""

import argparse
import bisect
import csv
import math
import statistics
from pathlib import Path


def read_rows(path):
    with path.open(newline="", encoding="utf-8-sig") as source:
        return list(csv.DictReader(source))


def percentile(values, fraction):
    if not values:
        return None
    ordered = sorted(values)
    at = fraction * (len(ordered) - 1)
    low = math.floor(at)
    high = math.ceil(at)
    return ordered[low] + (ordered[high] - ordered[low]) * (at - low)


def fmt(value, unit=""):
    return "n/a" if value is None else f"{value:.2f}{unit}"


def position(row):
    return tuple(float(row[axis]) for axis in ("px", "py", "pz"))


def compare_probes(sender_rows, receiver_rows, sender_id, receiver_id, label):
    sent = {int(row["input_sent"]) for row in sender_rows if row["kind"] == "bench_sent"
            and row["owner"] == sender_id and row["vehicle_id"] == receiver_id}
    received = {int(row["input_sent"]) for row in receiver_rows if row["kind"] == "bench_received"
                and row["owner"] == sender_id and row["vehicle_id"] == receiver_id}
    rtts = sorted((int(row["input_sent"]), float(row["lead_ms"])) for row in sender_rows
                  if row["kind"] == "bench_rtt" and row["owner"] == sender_id
                  and row["vehicle_id"] == receiver_id)
    if not sent:
        return
    delays = [delay for sequence, delay in rtts if sequence in sent]
    jitter = [abs(later[1] - earlier[1]) for earlier, later in zip(rtts, rtts[1:])]
    delivered = len(sent & received)
    print(f"  {label} probes: {delivered}/{len(sent)} delivered "
          f"({100 * (len(sent) - delivered) / len(sent):.1f}% loss); "
          f"RTT median {fmt(percentile(delays, .5), ' ms')}, "
          f"p95 {fmt(percentile(delays, .95), ' ms')}; "
          f"RTT variation p95 {fmt(percentile(jitter, .95), ' ms')}")


def compare(host_rows, guest_rows, guest_id):
    host_physics = [row for row in host_rows if row["kind"] == "physics" and row["owner"] == guest_id]
    guest_physics = [row for row in guest_rows if row["kind"] == "physics" and row["owner"] == guest_id]
    host_physics.sort(key=lambda row: float(row["server_time"]))
    host_times = [float(row["server_time"]) for row in host_physics]
    sent = {row["input_sent"]: float(row["server_time"]) for row in guest_rows
            if row["kind"] == "input_sent" and row["input_sent"] != "0"}
    input_transit_raw = [(float(row["server_time"]) - sent[row["input_received"]]) * 1000
                         for row in host_rows if row["kind"] == "input_received" and row["owner"] == guest_id
                         and row["input_received"] in sent]
    snapshot_errors = [float(row["error_m"]) for row in guest_rows if row["kind"] == "snapshot_received"]
    replay_counts = [int(row["replay_steps"]) for row in guest_rows
                     if row["kind"] == "snapshot_received" and "replay_steps" in row]
    snapshot_sends = {row["snapshot_tick"]: float(row["server_time"]) for row in host_rows
                      if row["kind"] == "snapshot_sent" and row["owner"] == guest_id}
    snapshot_transit_raw = [(float(row["server_time"]) - snapshot_sends[row["snapshot_tick"]]) * 1000
                            for row in guest_rows if row["kind"] == "snapshot_received"
                            and row["snapshot_tick"] in snapshot_sends]
    # The two NGO ServerTime estimates can disagree during a fresh connection. Matched messages
    # in both directions bound the offset; assume roughly symmetric median one-way delays.
    offset_ms = ((statistics.median(input_transit_raw) - statistics.median(snapshot_transit_raw)) / 2
                 if len(input_transit_raw) >= 5 and len(snapshot_transit_raw) >= 5 else None)
    input_transit = [value - offset_ms for value in input_transit_raw] if offset_ms is not None else []
    snapshot_transit = [value + offset_ms for value in snapshot_transit_raw] if offset_ms is not None else []
    distances = []
    if offset_ms is not None:
        for row in guest_physics:
            timestamp = float(row["server_time"]) + offset_ms / 1000
            index = bisect.bisect_left(host_times, timestamp)
            if index == 0 or index == len(host_times):
                continue
            before, after = host_physics[index - 1], host_physics[index]
            span = host_times[index] - host_times[index - 1]
            if span <= 0 or span > 0.2:
                continue
            blend = (timestamp - host_times[index - 1]) / span
            expected = tuple(a + (b - a) * blend for a, b in zip(position(before), position(after)))
            distances.append(math.dist(position(row), expected))
    print(f"Guest {guest_id}: {len(guest_physics)} local physics samples, {len(distances)} paired with host")
    print(f"  Estimated guest-to-host clock offset: {fmt(offset_ms, ' ms')}")
    print(f"  Host/guest position difference: median {fmt(percentile(distances, .5), ' m')}, "
          f"p95 {fmt(percentile(distances, .95), ' m')}, max {fmt(max(distances) if distances else None, ' m')}")
    print(f"  Input send to host receive: median {fmt(statistics.median(input_transit) if input_transit else None, ' ms')}, "
          f"p95 {fmt(percentile(input_transit, .95), ' ms')} ({len(input_transit)} matched inputs)")
    print(f"  Pre-correction snapshot error: median {fmt(percentile(snapshot_errors, .5), ' m')}, "
          f"p95 {fmt(percentile(snapshot_errors, .95), ' m')} ({len(snapshot_errors)} snapshots)")
    if replay_counts:
        print(f"  Snapshots with buffered movement replayed: {sum(count > 0 for count in replay_counts)}/{len(replay_counts)}, "
              f"p95 {fmt(percentile(replay_counts, .95))} physics steps")
    print(f"  Snapshot send to receive: median {fmt(percentile(snapshot_transit, .5), ' ms')}, "
          f"p95 {fmt(percentile(snapshot_transit, .95), ' ms')} ({len(snapshot_transit)} matched snapshots)")
    for label, rows, source, target in (("Host", host_rows, "0", guest_id),
                                         ("Guest", guest_rows, guest_id, "0")):
        live_ping = [float(row["lead_ms"]) for row in rows if row["kind"] == "live_ping"
                     and row["owner"] == source and row["vehicle_id"] == target]
        if live_ping:
            print(f"  {label} live ping: median {fmt(percentile(live_ping, .5), ' ms')}, "
                  f"p95 {fmt(percentile(live_ping, .95), ' ms')}")
    compare_probes(host_rows, guest_rows, "0", guest_id, "Host to guest")
    compare_probes(guest_rows, host_rows, guest_id, "0", "Guest to host")
    for label, rows in (("Host", host_rows), ("Guest", guest_rows)):
        frame_times = [float(row["lead_ms"]) for row in rows if row["kind"] == "bench_frame"]
        if frame_times:
            print(f"  {label} frame time: p95 {fmt(percentile(frame_times, .95), ' ms')}, "
                  f"max {fmt(max(frame_times), ' ms')}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path, help="Host capture folder containing host-*.csv and guest-*.csv")
    args = parser.parse_args()
    hosts = sorted(args.capture.glob("host-*.csv"))
    guests = sorted(args.capture.glob("guest-*.csv"))
    if len(hosts) != 1 or not guests:
        parser.error("capture folder needs one host-*.csv and at least one guest-*.csv")
    host_rows = read_rows(hosts[0])
    print(f"Capture {args.capture.name}: {len(host_rows)} host rows, {len(guests)} guest file(s)")
    for guest in guests:
        compare(host_rows, read_rows(guest), guest.stem.removeprefix("guest-"))
    print("Clock offset assumes similar median delay in both directions; one-way timing and position differences are estimates.")
    print("Probe loss counts datagrams missing from the receiver trace. RTT variation is the p95 absolute change between consecutive echoed probes.")


if __name__ == "__main__":
    main()
