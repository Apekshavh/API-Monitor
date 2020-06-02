using Microsoft.WindowsAzure.Storage.Blob.Protocol;
using Microsoft.WindowsAzure.Storage.Table;
using System.Globalization;

namespace SB.OrginationApi.Monitor
{
    public class orgAPITestBodyContent : TableEntity
    {
        public string QuoteName { get; set; }
        public string SerialNumber { get; set; }
        public string BodyContent { get; set; }

        public orgAPITestBodyContent()
        {

        }
        
        public orgAPITestBodyContent(string quoteName, string serialNumber)
        {
            this.PartitionKey = quoteName;
            this.RowKey = serialNumber;
        }
    }
}
