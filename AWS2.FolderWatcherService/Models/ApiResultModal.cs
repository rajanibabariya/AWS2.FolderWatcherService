using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AWS2.FolderWatcherService.Models
{
    class ApiResultModal
    {
        public int StatusCode { get; set; }
        public string Message { get; set; }
        public object Result { get; set; }
        public bool IsSuccess { get; set; }

        public ApiResultModal(int statusCode = 0, string message = "", object result = null, bool isSuccess = false)
        {
            this.StatusCode = statusCode;
            this.Message = message;
            this.Result = result;
            this.IsSuccess = isSuccess;
        }

    }
}
