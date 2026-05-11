using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public static class GestureRouter
{
    /// <summary>
    /// Legacy marker route — fires the synthetic TUIO-marker equivalent of a
    /// recognised gesture. HomePage uses this to "simulate marker placement".
    /// </summary>
    public static event Action<int> OnGestureMarker;

    /// <summary>
    /// Richer route — gesture name + score. Pages that need context-aware
    /// behaviour (EnrollmentPage letter cycling, LessonPage term cycling)
    /// subscribe here instead of the marker route.
    /// </summary>
    public static event Action<string, float> OnGestureRecognized;

    public static void RouteGesture(int markerId)
    {
        OnGestureMarker?.Invoke(markerId);
    }

    public static void RouteGestureRecognized(string name, float score)
    {
        OnGestureRecognized?.Invoke(name, score);
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

            // The Python server broadcasts both a typed envelope
            //   {"type":"gesture","gesture":"Circle","score":0.78}
            // and a legacy
            //   {"gesture":"Circle","score":0.78}
            // We only need to act once per recognition. Use the typed
            // envelope when present, otherwise the legacy shape.
            string envelopeType = json["type"]?.ToString();
            bool isLegacy = envelopeType == null && json.ContainsKey("gesture");
            bool isTyped  = envelopeType == "gesture";
            if (!isTyped && !isLegacy) return;
            if (isLegacy) return; // skip duplicate; the typed envelope is sent first

            string gesture = json["gesture"]?.ToString();
            if (string.IsNullOrEmpty(gesture)) return;

            float score = json["score"]?.Value<float>() ?? 0f;

            // Context-aware listeners
            GestureRouter.RouteGestureRecognized(gesture, score);

            // Legacy universal marker fallback — HomePage already maps a
            // marker placement to the right action, so emitting an integer
            // gives the simple path (login, enroll trigger, back) for free.
            int markerId = MapGestureToMarker(gesture);
            if (markerId != -1)
            {
                Console.WriteLine($"[GestureClient] {gesture} ({score:F2}) -> marker {markerId}");
                GestureRouter.RouteGesture(markerId);
            }
            else
            {
                Console.WriteLine($"[GestureClient] {gesture} ({score:F2}) -> context-only");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GestureClient] Parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Universal "if no page handles this gesture contextually" map.
    /// Pages that want different behaviour for a gesture subscribe to
    /// OnGestureRecognized directly and ignore the marker route.
    /// </summary>
    private int MapGestureToMarker(string gesture)
    {
        if (string.IsNullOrEmpty(gesture)) return -1;
        switch (gesture)
        {
            case "Circle":     return 10;  // HomePage enroll trigger
            case "Checkmark":  return 4;   // Universal "confirm / pick / keep"
            case "SwipeRight": return 7;   // Universal "next / advance / done"
            case "SwipeLeft":  return 20;  // Universal "back / cancel"

            // Legacy aliases (older gesture servers may still emit these)
            case "START":   return 3;
            case "CONFIRM": return 4;
            case "STOP":    return 20;
        }
        return -1;
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