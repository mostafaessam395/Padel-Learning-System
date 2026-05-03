using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuioDemo
{
    public class GazeProfile
    {
        public int Strokes_Score { get; set; } = 50;
        public int Rules_Score { get; set; } = 50;
        public int Practice_Score { get; set; } = 50;
        public int Quiz_Score { get; set; } = 50;
        public int Spelling_Score { get; set; } = 50;
        public int Competition_Score { get; set; } = 50;
    }

    public class UserData
    {
        public string UserId { get; set; }
        public string BluetoothId { get; set; }    
        public string FaceId { get; set; }
        public string Name { get; set; }
        public string Gender { get; set; }
        public int Age { get; set; }
        public string Level { get; set; }
        public GazeProfile GazeProfile { get; set; }
    }
}
