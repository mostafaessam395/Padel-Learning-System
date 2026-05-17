using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public static class ExpressionRouter
{
    public static event Action<string> OnEmotionDetected;

    public static void RouteEmotion(string emotion)
    {
        OnEmotionDetected?.Invoke(emotion);
    }
}

public class ExpressionClient : IDisposable
{
    private TcpClient _client;
    private NetworkStream _stream;
    private bool _isRunning;
    private readonly byte[] _buffer = new byte[4096];
    private StringBuilder _messageBuffer = new StringBuilder();

    public bool IsConnected => _client?.Connected ?? false;

    public void Connect(string host = "127.0.0.1", int port = 5005)
    {
        try
        {
            Console.WriteLine($"[ExpressionClient] Connecting to {host}:{port}...");
            _client = new TcpClient();
            _client.Connect(host, port);
            _stream = _client.GetStream();
            _isRunning = true;
            Console.WriteLine($"[ExpressionClient] Connected!");
            Task.Factory.StartNew(ReceiveLoop, TaskCreationOptions.LongRunning);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ExpressionClient] Connection failed: {ex.Message}");
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
            if (type == "emotion")
            {
                string emotion = json["emotion"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(emotion))
                    ExpressionRouter.RouteEmotion(emotion.ToLower());
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
