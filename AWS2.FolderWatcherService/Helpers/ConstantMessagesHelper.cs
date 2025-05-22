using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AWS2.FolderWatcherService.Helpers
{
    class ConstantMessagesHelper
    {
        public static string companyName = "Azista Industries Pvt. Ltd.";
        public static string applicationName = "AWS2 - Folder Watcher Service";
    }

    public class APIURLList
    {
        public const string BaseURL = "http://20.198.113.129:8891/api/";
        public static string ReceivesStationEnvDataAPI = "DataLoggerReceiver/StationEnvDataReceives/ReceivesStationEnvData/{clientCode}/{transMode}/{hostDetail}";
        public const string ReceivesFileLogsAPI = "DataLoggerReceiver/StationEnvDataReceives/ReceivesFileLogs/{clientCode}/{hostDetail}";
    }
}
