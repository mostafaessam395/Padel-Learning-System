using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// Routes gaze tracking events to subscribed listeners.
/// </summary>
public static class GazeRouter
{
    /// <summary>
    /// Fired when a gaze point is received. Parameters: normalized x (0-1), y (0-1).
    /// </summary>
    public static event Action<float, float> OnGazePoint;

    public static void RouteGaze(float x, float y)
    {
        OnGazePoint?.Invoke(x, y);
    }
}

/// <summary>
/// TCP client connecting to the Python gaze-tracking server on localhost:5002.
/// Protocol: server sends newline-delimited JSON:
///   {"type":"gaze","x":0.45,"y":0.62}
/// </summary>
public class GazeClient : IDisposable
{
    private TcpClient _client;
    private NetworkStream _stream;
    private bool _isRunning;
    private readonly byte[] _buffer = new byte[4096];
    private StringBuilder _messageBuffer = new StringBuilder();

    public bool IsConnected => _client?.Connected ?? false;

    public void Connect(string host = "127.0.0.1", int port = 5002)
    {
        try
        {
            Cleanup();
            Console.WriteLine($"[GazeClient] Connecting to {host}:{port}...");
            _client = new TcpClient();
            _client.Connect(host, port);
            _stream = _client.GetStream();
            _isRunning = true;
            Console.WriteLine($"[GazeClient] Connected!");
            Task.Factory.StartNew(ReceiveLoop, TaskCreationOptions.LongRunning);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GazeClient] Connection failed: {ex.Message}");
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
                _messageBuffer.Append(Encoding.UTF8.GetString(_buffer, 0, bytesRead));
                ProcessBuffer();
            }
        }
        catch { }
        finally { Cleanup(); }
    }

    private void ProcessBuffer()
    {
        string buffer = _messageBuffer.ToString();
        int pos;
        while ((pos = buffer.IndexOf('\n')) >= 0)
        {
            string line = buffer.Substring(0, pos).Trim();
            buffer = buffer.Substring(pos + 1);
            if (!string.IsNullOrWhiteSpace(line))
                ProcessMessage(line);
        }
        _messageBuffer.Clear();
        _messageBuffer.Append(buffer);
    }

    private void ProcessMessage(string data)
    {
        try
        {
            int s = data.IndexOf('{'), e = data.LastIndexOf('}');
            if (s == -1 || e <= s) return;
            JObject json = JObject.Parse(data.Substring(s, e - s + 1));
            string type = json["type"]?.ToString() ?? "";
            if (type == "gaze")
            {
                float x = json["x"]?.Value<float>() ?? -1f;
                float y = json["y"]?.Value<float>() ?? -1f;
                if (x >= 0 && y >= 0)
                    GazeRouter.RouteGaze(x, y);
            }
        }
        catch { }
    }

    public void Disconnect() { _isRunning = false; Cleanup(); }

    private void Cleanup()
    {
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        _stream = null; _client = null;
    }

    public void Dispose() { Disconnect(); }
}
