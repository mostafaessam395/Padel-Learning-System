using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TuioDemo;

/// <summary>
/// CRUD service for users stored in Data/users.json (runtime copy in bin/Debug/Data/).
/// The .csproj sets CopyToOutputDirectory=Never for users.json so rebuilds never
/// overwrite runtime edits. On first run, we seed from the project-source copy if
/// the runtime file does not yet exist.
/// </summary>
public class UserService
{
    // Runtime path — where the running app reads and writes
    private static readonly string RuntimePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "users.json");

    // Log path
    private static readonly string LogPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_save_debug_log.txt");

    // ── Public API ────────────────────────────────────────────────────────

    public List<UserData> LoadAll()
    {
        try
        {
            EnsureRuntimeFile();

            Log($"LOAD  path={RuntimePath}");

            if (!File.Exists(RuntimePath))
            {
                Log("LOAD  file not found — returning empty list");
                return new List<UserData>();
            }

            string raw = File.ReadAllText(RuntimePath).Trim();
            if (string.IsNullOrEmpty(raw) || raw == "[]")
            {
                Log("LOAD  file empty — returning empty list");
                return new List<UserData>();
            }

            var list = JsonConvert.DeserializeObject<List<UserData>>(raw)
                       ?? new List<UserData>();

            // Assign stable deterministic UserId to any user that lacks one.
            // Deterministic = based on Name+BluetoothId so the same user always
            // gets the same ID even across restarts before the first save.
            foreach (var u in list.Where(u => string.IsNullOrEmpty(u.UserId)))
                u.UserId = StableId(u);

            Log($"LOAD  loaded {list.Count} user(s)");
            return list;
        }
        catch (Exception ex)
        {
            Log($"LOAD  ERROR: {ex.Message}");
            return new List<UserData>();
        }
    }

    public void AddUser(UserData u)
    {
        var list = LoadAll();
        if (string.IsNullOrEmpty(u.UserId)) u.UserId = StableId(u);
        list.Add(u);
        Save(list, $"ADD user={u.UserId} name={u.Name}");
    }

    public void UpdateUser(UserData u)
    {
        var list = LoadAll();
        int idx = list.FindIndex(x => x.UserId == u.UserId);
        if (idx >= 0)
        {
            // Preserve GazeProfile if not supplied
            if (u.GazeProfile == null) u.GazeProfile = list[idx].GazeProfile;
            list[idx] = u;
            Save(list, $"UPDATE user={u.UserId} name={u.Name}");
        }
        else
        {
            list.Add(u);
            Save(list, $"ADD(via update) user={u.UserId} name={u.Name}");
        }
    }

    public void DeleteUser(string userId)
    {
        var list = LoadAll();
        list.RemoveAll(x => x.UserId == userId);
        Save(list, $"DELETE user={userId}");
    }

    public void DeactivateUser(string userId)
    {
        var list = LoadAll();
        var u = list.FirstOrDefault(x => x.UserId == userId);
        if (u != null) u.IsActive = false;
        Save(list, $"DEACTIVATE user={userId}");
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private void Save(List<UserData> list, string reason)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RuntimePath));
            string json = JsonConvert.SerializeObject(list, Formatting.Indented);
            File.WriteAllText(RuntimePath, json);

            // Verify write succeeded
            string verify = File.ReadAllText(RuntimePath);
            Log($"SAVE  path={RuntimePath}  users={list.Count}  reason={reason}  verified={verify.Length > 2}");
        }
        catch (Exception ex)
        {
            Log($"SAVE  ERROR: {ex.Message}  reason={reason}");
        }
    }

    /// <summary>
    /// On first run, copy the project-source users.json into the runtime Data folder
    /// so the app has seed data. Only copies if the runtime file does not exist.
    /// </summary>
    private static void EnsureRuntimeFile()
    {
        if (File.Exists(RuntimePath)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(RuntimePath));

        // Try to find the project-source copy (two levels up from bin/Debug)
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string sourcePath = Path.GetFullPath(
            Path.Combine(baseDir, "..", "..", "Data", "users.json"));

        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, RuntimePath, overwrite: false);
            Log($"SEED  copied source {sourcePath} → {RuntimePath}");
        }
        else
        {
            // Create empty array so the file exists
            File.WriteAllText(RuntimePath, "[]");
            Log($"SEED  created empty {RuntimePath} (source not found at {sourcePath})");
        }
    }

    /// <summary>
    /// Generates a stable, deterministic UserId from Name + BluetoothId.
    /// Same inputs always produce the same ID, so users without a stored
    /// UserId get a consistent ID across restarts.
    /// </summary>
    private static string StableId(UserData u)
    {
        string key = (u.Name ?? "") + "|" + (u.BluetoothId ?? "") + "|" + (u.FaceId ?? "");
        int hash = key.GetHashCode();
        return "usr_" + Math.Abs(hash).ToString("x8");
    }

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n");
        }
        catch { }
    }
}
