using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace TuioDemo
{
    public class UserLoader
    {
        public static List<UserData> LoadUsers(string path)
        {
            if (!File.Exists(path))
                return new List<UserData>();

            string json = File.ReadAllText(path);

            return JsonConvert.DeserializeObject<List<UserData>>(json);
        }
    }
}