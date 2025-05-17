using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AWS2.FolderWatcherService.Helpers
{
    class ConstantMessagesHelper
    {
    }

    public class APIURLList
    {
        public const string BaseURL = "https://localhost:44396/api/";
        public static string ReceivesStationEnvDataAPI = "{BaseURL}DataLoggerReceiver/StationEnvDataReceives/ReceivesStationEnvData/{clientCode}/{transMode}/{hostDetail}";
        public static string clientCode = "", transMode = "", hostDetail = "";
    }
}
