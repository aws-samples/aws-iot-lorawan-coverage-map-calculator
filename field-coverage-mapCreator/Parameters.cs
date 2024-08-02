using System;

namespace field_coverage_mapCreator
{
    public class Parameters
    {
        public string AWSProfile { get; set; }
        public string DatabaseName { get; set; }
        public string TableName { get; set; }
        public string CrawlerName { get; set; }
        public string OutputS3BucketName { get; set; }
        public string OutputPath { get; set; }
        public string H3Resolution { get; set; }
        public string ShapeFilePath { get; set; }
        public string TimeFilter { get; set; }

        public string DeviceFilter {get;set;}

        public Coordinate Coordinate { get; set; }
        public bool TriggerCrawler { get; set; }
    }

    public class Coordinate
    {
        public double Longitude { get; set; }
        public double Latitude { get; set; }
    }
}