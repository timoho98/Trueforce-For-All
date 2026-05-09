// One-shot UDP port discovery for the "user picked a non-default port"
// case. Opens listeners on every candidate port, blocks for up to
// timeoutMs, returns the first port that received a packet matching the
// caller's validator.
//
// Design intent:
//   - Caller has already failed to receive on its primary port and wants
//     to know if the game is sending to a different port instead.
//   - We open multiple sockets simultaneously and poll all of them with
//     Socket.Select so a single thread does the wait. No thread-per-port.
//   - ReuseAddress=true so we coexist with any other listener already
//     bound to one of the candidates (SimHub itself, Sim Racing Studio).
//   - Skip candidates that fail to bind for any other reason — partial
//     coverage is better than failing the scan entirely.
//   - Validator decides whether a received packet is the kind we want
//     (F1 25 PacketFormat=2025, Forza Sled-shape, etc). A false validator
//     simply discards and the scan keeps listening.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TrueforceForAll.Core
{
    public static class UdpPortScanner
    {
        /// <summary>Listens on every candidate port at once for up to
        /// <paramref name="timeoutMs"/>. Returns the first port number that
        /// received a packet for which <paramref name="validator"/> returned
        /// true, or 0 on timeout / cancel / no candidates bound.
        /// Synchronous: runs on the calling thread. Caller is expected to
        /// invoke this from a background thread / Task.Run so it doesn't
        /// block the UI.</summary>
        public static int Scan(IReadOnlyList<int> candidatePorts, IPAddress bindAddress,
            Func<byte[], int, bool> validator, int timeoutMs, CancellationToken cancel)
        {
            if (candidatePorts == null || candidatePorts.Count == 0) return 0;
            if (validator == null) throw new ArgumentNullException(nameof(validator));
            if (bindAddress == null) bindAddress = IPAddress.Any;

            // Try to bind every candidate. Skip the ones that fail (already
            // in use without ReuseAddress, permission-denied, etc.) so a
            // single bad port doesn't ruin the scan.
            var sockets = new List<Socket>();
            try
            {
                foreach (int port in candidatePorts)
                {
                    Socket s = null;
                    try
                    {
                        s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                        s.ExclusiveAddressUse = false;
                        s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        s.Bind(new IPEndPoint(bindAddress, port));
                        sockets.Add(s);
                    }
                    catch
                    {
                        try { s?.Close(); } catch { }
                    }
                }

                if (sockets.Count == 0) return 0;

                byte[] buf = new byte[2048];
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (cancel.IsCancellationRequested) return 0;

                    // Build a fresh readable list each iteration; Select
                    // mutates it to contain only the ready sockets on return.
                    var readable = new List<Socket>(sockets);
                    int remainingMs = (int)Math.Max(0, timeoutMs - sw.ElapsedMilliseconds);
                    // Cap the wait so we revisit the cancel check periodically.
                    int waitUs = Math.Min(remainingMs, 250) * 1000;
                    Socket.Select(readable, null, null, waitUs);

                    foreach (var s in readable)
                    {
                        EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                        int len;
                        try
                        {
                            len = s.ReceiveFrom(buf, 0, buf.Length, SocketFlags.None, ref ep);
                        }
                        catch (SocketException)
                        {
                            continue;
                        }
                        if (len <= 0) continue;
                        if (validator(buf, len))
                        {
                            // Resolve which port this socket is bound to.
                            var local = s.LocalEndPoint as IPEndPoint;
                            if (local != null) return local.Port;
                        }
                    }
                }
            }
            finally
            {
                foreach (var s in sockets)
                {
                    try { s.Close(); } catch { }
                    try { s.Dispose(); } catch { }
                }
            }
            return 0;
        }
    }
}
