using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public static class GestureRouter
{
    public static event Action<int> OnGestureMarker;

    public static void RouteGesture(int markerId)
    {
        OnGestureMarker?.Invoke(markerId);
    }
}

public class GestureClient : IDisposable
{
    private TcpClient _client;
    private NetworkStream _stream;
    private bool _isRunning;
    private readonly byte[] _buffer = new byte[4096];
    private StringBuilder _messageBuffer = new StringBuilder();

    public bool IsConnected => _client?.Connected ?? false;

    public void Connect(string host = "127.0.0.1", int port = 5000)
    {
        try
        {
            Console.WriteLine($"[GestureClient] Connecting to {host}:{port}...");
            _client = new TcpClient();
            _client.Connect(host, port);
            _stream = _client.GetStream();
            _isRunning = true;
            Console.WriteLine($"[GestureClient] Connected!");

            Task.Factory.StartNew(ReceiveLoop, TaskCreationOptions.LongRunning);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GestureClient] Connection failed: {ex.Message}");
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
            Console.WriteLine($"[GestureClient] Error: {ex.Message}");
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
                ProcessGesture(line);
            }
        }
        _messageBuffer.Clear();
        _messageBuffer.Append(buffer);
    }

    private void ProcessGesture(string data)
    {
        try
        {
            int start = data.IndexOf('{');
            int end = data.LastIndexOf('}');
            if (start == -1 || end == -1 || end <= start) return;

            string jsonStr = data.Substring(start, end - start + 1);
            JObject json = JObject.Parse(jsonStr);

            if (json.ContainsKey("gesture"))
            {
                string gesture = json["gesture"].ToString();
                int markerId = MapGestureToMarker(gesture);
                if (markerId != -1)
                {
                    Console.WriteLine($"[GestureClient] {gesture} -> Marker {markerId}");
                    GestureRouter.RouteGesture(markerId);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GestureClient] Parse error: {ex.Message}");
        }
    }

    private int MapGestureToMarker(string gesture)
    {
        switch (gesture?.ToUpper())
        {
            case "START": return 3;   // point_up → Vocabulary
            case "CONFIRM": return 4;   // open_palm → Grammar
            case "STOP": return 20;  // stop_sign → Back
            default: return -1;
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