using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// Routes face recognition events to all subscribed listeners across the application.
/// </summary>
public static class FaceIDRouter
{
    /// <summary>Fired when a confident face match arrives. (name, confidence 0-1)</summary>
    public static event Action<string, float> OnFaceRecognized;

    /// <summary>
    /// Fired on every face_scan event (matched or not). userName is null when unmatched.
    /// Used by the HomePage live-confidence ticker.
    /// </summary>
    public static event Action<string, float, bool> OnFaceScanProgress;

    /// <summary>Fired on enroll/reload/cancel replies from the Python server.</summary>
    public static event Action<JObject> OnServerReply;

    public static void RouteRecognition(string userName, float confidence)
    {
        OnFaceRecognized?.Invoke(userName, confidence);
    }

    public static void RouteScanProgress(string userName, float confidence, bool matched)
    {
        OnFaceScanProgress?.Invoke(userName, confidence, matched);
    }

    public static void RouteServerReply(JObject json)
    {
        OnServerReply?.Invoke(json);
    }
}

/// <summary>
/// TCP client that connects to the Python face-recognition server on localhost:5001.
/// Mirrors the architecture of GestureClient.cs for consistency.
/// Protocol: server sends newline-delimited JSON:
///   {"type":"face_detected","user_name":"Ahmed","confidence":0.92}
/// </summary>
public class FaceIDClient : IDisposable
{
    private TcpClient _client;
    private NetworkStream _stream;
    private bool _isRunning;
    private readonly byte[] _buffer = new byte[4096];
    private StringBuilder _messageBuffer = new StringBuilder();

    public bool IsConnected => _client?.Connected ?? false;

    public void Connect(string host = "127.0.0.1", int port = 5001)
    {
        try
        {
            // Clean up any previous connection before reconnecting
            Cleanup();

            Console.WriteLine($"[FaceIDClient] Connecting to {host}:{port}...");
            _client = new TcpClient();
            _client.Connect(host, port);
            _stream = _client.GetStream();
            _isRunning = true;
            Console.WriteLine($"[FaceIDClient] Connected!");

            Task.Factory.StartNew(ReceiveLoop, TaskCreationOptions.LongRunning);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIDClient] Connection failed: {ex.Message}");
            Cleanup();
        }
    }

    private void ReceiveLoop()
    {
        try
        {
            while (_isRunning && _client != null && _client.Connected)
            {
                int bytesRead = _stream.Read(_buffer, 0, _buffer.Length);
                if (bytesRead == 0) break;

                string data = Encoding.UTF8.GetString(_buffer, 0, bytesRead);
                _messageBuffer.Append(data);
                ProcessBuffer();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIDClient] Error: {ex.Message}");
        }
        finally
        {
            Cleanup();
        }
    }

    private void ProcessBuffer()
    {
        string buffer = _messageBuffer.ToString();
        int newlinePos;
        while ((newlinePos = buffer.IndexOf('\n')) >= 0)
        {
            string line = buffer.Substring(0, newlinePos).Trim();
            buffer = buffer.Substring(newlinePos + 1);
            if (!string.IsNullOrWhiteSpace(line))
            {
                ProcessMessage(line);
            }
        }
        _messageBuffer.Clear();
        _messageBuffer.Append(buffer);
    }

    private void ProcessMessage(string data)
    {
        try
        {
            int start = data.IndexOf('{');
            int end = data.LastIndexOf('}');
            if (start == -1 || end == -1 || end <= start) return;

            string jsonStr = data.Substring(start, end - start + 1);
            JObject json = JObject.Parse(jsonStr);

            string type = json["type"]?.ToString() ?? "";

            switch (type)
            {
                case "face_detected":
                {
                    string userName = json["user_name"]?.ToString();
                    float confidence = json["confidence"]?.Value<float>() ?? 0f;
                    if (!string.IsNullOrEmpty(userName))
                    {
                        Console.WriteLine($"[FaceIDClient] Match: {userName} ({confidence:F2})");
                        FaceIDRouter.RouteRecognition(userName, confidence);
                    }
                    break;
                }
                case "face_scan":
                {
                    string userName = (json["user_name"] == null || json["user_name"].Type == JTokenType.Null)
                        ? null
                        : json["user_name"].ToString();
                    float confidence = json["confidence"]?.Value<float>() ?? 0f;
                    bool matched = json["matched"]?.Value<bool>() ?? false;
                    FaceIDRouter.RouteScanProgress(userName, confidence, matched);
                    break;
                }
                case "enroll_done":
                case "enroll_failed":
                case "enroll_cancel_done":
                case "reload_done":
                {
                    Console.WriteLine($"[FaceIDClient] Reply: {type} {json}");
                    FaceIDRouter.RouteServerReply(json);
                    break;
                }
                default:
                    Console.WriteLine($"[FaceIDClient] Unhandled type='{type}' raw={data}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIDClient] Parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a single newline-terminated JSON command to the face server.
    /// Returns true on apparent success, false otherwise. Non-blocking.
    /// </summary>
    public bool SendCommand(JObject command)
    {
        if (command == null) return false;
        var stream = _stream;
        var client = _client;
        if (stream == null || client == null || !client.Connected) return false;

        try
        {
            string line = command.ToString(Newtonsoft.Json.Formatting.None) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            Console.WriteLine($"[FaceIDClient] -> {line.TrimEnd()}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FaceIDClient] SendCommand error: {ex.Message}");
            return false;
        }
    }

    public void Disconnect()
    {
        _isRunning = false;
        Cleanup();
    }

    private void Cleanup()
    {
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        _stream = null;
        _client = null;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
