using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Orhex
{
    internal class DiscordRpc
    {
        private NamedPipeClientStream pipe;
        private bool connected;
        private bool running;
        private readonly object sync = new object();

        public bool IsConnected { get { return connected; } }

        public bool Connect(string applicationId)
        {
            Close();
            if (!IsDiscordRunning()) return false;
            for (int i = 0; i < 5; i++)
            {
                NamedPipeClientStream p = null;
                try
                {
                    p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    p.Connect(1200);
                    WriteFrame(p, 0, "{\"v\":1,\"client_id\":\"" + applicationId + "\"}");
                    byte[] resp = ReadFrame(p, 1200);
                    if (resp == null || resp.Length == 0)
                    {
                        p.Dispose();
                        continue;
                    }
                    pipe = p;
                    connected = true;
                    running = true;
                    Thread th = new Thread(ReadLoop);
                    th.IsBackground = true;
                    th.Start();
                    return true;
                }
                catch
                {
                    if (p != null)
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            return false;
        }

        public bool SetActivity(string details, string state, long startMs, long endMs)
        {
            if (!connected || pipe == null) return false;
            lock (sync)
            {
                try
                {
                    var sb = new StringBuilder(128);
                    sb.Append("{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":");
                    sb.Append(Process.GetCurrentProcess().Id);
                    sb.Append(",\"activity\":{");
                    bool first = true;
                    if (!string.IsNullOrEmpty(details))
                    {
                        sb.Append("\"details\":\"").Append(Esc(details)).Append('"');
                        first = false;
                    }
                    if (!string.IsNullOrEmpty(state))
                    {
                        if (!first) sb.Append(',');
                        sb.Append("\"state\":\"").Append(Esc(state)).Append('"');
                        first = false;
                    }
                    if (endMs > 0)
                    {
                        if (!first) sb.Append(',');
                        sb.Append("\"timestamps\":{\"start\":").Append(startMs)
                          .Append(",\"end\":").Append(endMs).Append('}');
                    }
                    sb.Append("}},\"nonce\":\"").Append(Guid.NewGuid().ToString("N")).Append("\"}");
                    WriteFrame(pipe, 1, sb.ToString());
                    return true;
                }
                catch
                {
                    connected = false;
                    return false;
                }
            }
        }

        public void Close()
        {
            running = false;
            lock (sync)
            {
                if (pipe != null)
                {
                    try { pipe.Dispose(); } catch { }
                    pipe = null;
                }
                connected = false;
            }
        }

        private static bool IsDiscordRunning()
        {
            try
            {
                foreach (Process p in Process.GetProcesses())
                    if (p.ProcessName.StartsWith("Discord", StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { }
            return false;
        }

        private void ReadLoop()
        {
            while (running && pipe != null)
            {
                byte[] frame;
                try { frame = ReadFrame(pipe, 3000); }
                catch { break; }
                if (frame == null) break;
                if (frame.Length == 0) continue;
                int opcode = frame[0];
                if (opcode == 3)
                {
                    byte[] payload = new byte[frame.Length - 8];
                    Array.Copy(frame, 8, payload, 0, payload.Length);
                    lock (sync)
                    {
                        if (pipe != null)
                        {
                            try
                            {
                                byte[] outFrame = BuildFrame(4, payload);
                                pipe.Write(outFrame, 0, outFrame.Length);
                                pipe.Flush();
                            }
                            catch { }
                        }
                    }
                }
            }
            connected = false;
        }

        private static byte[] BuildFrame(byte opcode, byte[] payload)
        {
            byte[] frame = new byte[8 + payload.Length];
            frame[0] = opcode;
            frame[4] = (byte)(payload.Length & 0xFF);
            frame[5] = (byte)((payload.Length >> 8) & 0xFF);
            frame[6] = (byte)((payload.Length >> 16) & 0xFF);
            frame[7] = (byte)((payload.Length >> 24) & 0xFF);
            Array.Copy(payload, 0, frame, 8, payload.Length);
            return frame;
        }

        private static void WriteFrame(NamedPipeClientStream p, byte opcode, string json)
        {
            byte[] frame = BuildFrame(opcode, Encoding.UTF8.GetBytes(json));
            p.Write(frame, 0, frame.Length);
            p.Flush();
        }

        private static byte[] ReadFrame(NamedPipeClientStream p, int timeoutMs)
        {
            byte[] header = new byte[8];
            int got = 0;
            while (got < 8)
            {
                int n = ReadWithTimeout(p, header, got, 8 - got, timeoutMs);
                if (n == -1)
                {
                    if (got == 0) return new byte[0];
                    return null;
                }
                if (n == 0) return null;
                got += n;
            }
            int len = header[4] | (header[5] << 8) | (header[6] << 16) | (header[7] << 24);
            if (len < 0 || len > 262144) return null;
            byte[] buf = new byte[len];
            got = 0;
            while (got < len)
            {
                int n = ReadWithTimeout(p, buf, got, len - got, timeoutMs);
                if (n <= 0) return null;
                got += n;
            }
            return buf;
        }

        private static int ReadWithTimeout(NamedPipeClientStream p, byte[] buffer, int offset, int count, int timeoutMs)
        {
            IAsyncResult ar = p.BeginRead(buffer, offset, count, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return -1;
            return p.EndRead(ar);
        }

        private static string Esc(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
