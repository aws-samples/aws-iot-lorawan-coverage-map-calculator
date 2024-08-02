using System;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;

using Amazon.Athena;
using Amazon.Athena.Model;

using Amazon.S3;
using Amazon.S3.Model;

using H3;
using H3.Algorithms;
using H3.Extensions;

using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite;
using NetTopologySuite.IO.Esri;

using CsvHelper;
using CsvHelper.Configuration;
using H3.Model;
using Amazon.Athena.Model.Internal.MarshallTransformations;
using Amazon.Glue;
using Amazon.Glue.Model;

using System.Text.Json;
using System.Text;



namespace field_coverage_mapCreator
{
    class Program
    {
        //those are the Athena and S3 clients
        static IAmazonAthena _athenaClient;
        static IAmazonS3 _s3Client;
        static IAmazonGlue _glueClient;

        static async Task Main(string[] args)
        {
            Console.WriteLine("Map Creator tool started!!!");

            // Load configuration from JSON file
            var config = LoadConfiguration("config.json");

            if ( !string.IsNullOrEmpty(config.AWSProfile) )
            {
                // Set the environment variable to use a specific AWS profile
                Environment.SetEnvironmentVariable("AWS_PROFILE", config.AWSProfile);
                Console.WriteLine($"Forced AWS_PROFILE { config.AWSProfile }");
            }
    
            // create athena and s3 clients
            await CreateClients();


            // trigger the crawler and wait for it completion if required
            if(config.TriggerCrawler)
            {
                // Trigger the Glue crawler and wait for its completion              
                var startStatus = await StartGlueCrawler(_glueClient, config.CrawlerName);

                if (startStatus == "OK")
                {
                    //just await a bit before starting polling for the crawler state
                    await Task.Delay(500);

                    Console.WriteLine($"Crawler {config.CrawlerName} started successfully.");
                    var crawlerStatus = await PollCrawlerStatus(_glueClient, config.CrawlerName);
                    if (crawlerStatus == "SUCCEEDED")
                    {
                        Console.WriteLine("Crawler completed successfully.");
                    }
                    else
                    {
                        Console.WriteLine("Crawler did not complete successfully.");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("Failed to start the crawler.");
                    return;
                }   
            }


            string locationFilter = null;
                      
            if ( config.Coordinate != null)
            {
                // Flatten the sequence of sequences into a single sequence
                var coordinate = new NetTopologySuite.Geometries.Coordinate(config.Coordinate.Longitude, config.Coordinate.Latitude);

                var h3Indexes = (from h3Index in coordinate.ToH3Index(5).GetNeighbours()            
                                from childIndex in h3Index.GetChildrenForResolution(7)
                                select childIndex)
                                .Distinct(); // Optional: Remove duplicates if necessary

                GenerateFilteringAreaShapefile(h3Indexes, config.ShapeFilePath + "_filter");

                var h3IndexesString = "'" + string.Join("','", h3Indexes).ToString() + "'";

                locationFilter =  $"prt_h7 IN ({h3IndexesString})";
            }


            //create the where filter
            string whereClause = string.Empty;

            if (!string.IsNullOrEmpty(config.TimeFilter) || !string.IsNullOrEmpty(locationFilter) || !string.IsNullOrEmpty(config.DeviceFilter))
            {
                StringBuilder whereClauseBuilder = new StringBuilder();
                whereClauseBuilder.Append("WHERE "); 

                bool firstConditionAdded = false;

                if (!string.IsNullOrEmpty(config.TimeFilter))
                {
                    whereClauseBuilder.Append(config.TimeFilter);
                    firstConditionAdded = true;
                }

                if (!string.IsNullOrEmpty(locationFilter))
                {
                    if (firstConditionAdded) 
                    {
                        whereClauseBuilder.Append(" AND ");
                    }
                    whereClauseBuilder.Append(locationFilter);
                    firstConditionAdded = true;
                }

                if (!string.IsNullOrEmpty(config.DeviceFilter))
                {
                    if (firstConditionAdded) 
                    {
                        whereClauseBuilder.Append(" AND ");
                    }
                    whereClauseBuilder.Append(config.DeviceFilter);
                }

                whereClause = whereClauseBuilder.ToString();
            }
            //base aggregationq query
            string aggregatesQuery = @"
SELECT  
    prt_**H3_Resolution** as H3Index,                      
    COUNT(*) AS SampleCount,
    ROUND(AVG(COALESCE(CAST(statistics.NumberOfGatewayReceivingUplink AS DOUBLE), 0)), 2) AS AvgNumberOfGatewayReceivingUplink,
    ROUND(AVG(COALESCE(CAST(statistics.MaxDistance AS DOUBLE), 0)), 2) AS AvgMaxDistance,
    ROUND(AVG(COALESCE(CAST(statistics.AvgDistance AS DOUBLE), 0)), 2) AS AvgDistance,
    ROUND(AVG(COALESCE(CAST(statistics.MinDistance AS DOUBLE), 0)), 2) AS AvgMinDistance,
    ROUND(AVG(COALESCE(CAST(statistics.MaxRSSI AS DOUBLE), 0)), 2) AS AvgMaxRssi,
    ROUND(AVG(COALESCE(CAST(statistics.AvgRSSI AS DOUBLE), 0)), 2) AS AvgRssi,
    ROUND(AVG(COALESCE(CAST(statistics.MinRSSI AS DOUBLE), 0)), 2) AS AvgMinRssi,
    ROUND(AVG(COALESCE(CAST(statistics.MaxSnr AS DOUBLE), 0)), 2) AS AvgMaxSnr,
    ROUND(AVG(COALESCE(CAST(statistics.AvgSnr AS DOUBLE), 0)), 2) AS AvgSnr,
    ROUND(AVG(COALESCE(CAST(statistics.MinSnr AS DOUBLE), 0)), 2) AS AvgMinSnr
FROM **TABLE**
**WHERE**
GROUP BY prt_**H3_Resolution**";
                        
            aggregatesQuery = aggregatesQuery.Replace("**WHERE**", whereClause)
                                             .Replace("**H3_Resolution**", config.H3Resolution)
                                             .Replace("**TABLE**", $"\"{config.DatabaseName}\".\"{config.TableName}\"");
            

            //execute the athena query to aggregate message received by lorawan gps enabled device            
            var aggregatesQueryExecutionId = await ExecuteAthenaQuery(_athenaClient, aggregatesQuery, $"s3://" + config.OutputS3BucketName + "/" + config.OutputPath + "/");


            if (!string.IsNullOrEmpty(aggregatesQueryExecutionId))
            {
                Console.WriteLine($"Query start execution successfully. Execution ID: {aggregatesQueryExecutionId}");

                var aggregatesQueryExecutionStatus = await PollQueryExecutionStatus(_athenaClient, aggregatesQueryExecutionId);
                Console.WriteLine($"Query Execution Status: {aggregatesQueryExecutionStatus}");

                if (aggregatesQueryExecutionStatus == QueryExecutionState.SUCCEEDED)
                {
                    // Query succeeded, proceed to load the CSV from S3
                    Console.WriteLine("Query succeeded. You can now proceed to load the CSV results from S3.");
                    
                    var resultContent = await DownloadCsvFromS3Async(_s3Client, config.OutputS3BucketName, $"{config.OutputPath}/{aggregatesQueryExecutionId}.csv");

                    var resultSet = ParseCsvContent<CoverageAggregate>(resultContent);

                    Console.WriteLine("Result loaded now proceding to generate the coverage map.");

                    //now geneate the shapeFile with the bounding box of the H3 layer and the aggregates loaded from the Athena Query results
                    GenerateShapefileFromAggregates(resultSet, config.ShapeFilePath);
                }
                else
                {
                    Console.WriteLine("Query did not succeed. Please check the query execution details for errors.");
                }           
            }
            else
            {
                Console.WriteLine("Query execution failed.");
            }
        }

        static async Task CreateClients()
        {
            //create the clients
            _athenaClient = new AmazonAthenaClient(); 
            _s3Client = new AmazonS3Client(); 
            _glueClient = new AmazonGlueClient();
        }

        static Parameters LoadConfiguration(string filePath)
        {
            var jsonString = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Parameters>(jsonString);
        }

        static async Task<string> StartGlueCrawler(IAmazonGlue client, string crawlerName)
        {
            try
            {
                var request = new StartCrawlerRequest
                {
                    Name = crawlerName
                };
                var response = await client.StartCrawlerAsync(request);
                return response.HttpStatusCode.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting Glue Crawler: {ex.Message}");
                return null;
            }
        }

        static async Task<string> PollCrawlerStatus(IAmazonGlue client, string crawlerName, int maxAttempts = 30, int delayInSeconds = 10)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var request = new GetCrawlerRequest { Name = crawlerName };
                var response = await client.GetCrawlerAsync(request);
                var status = response.Crawler.State;

                if (status == CrawlerState.READY)
                {
                    return "SUCCEEDED";
                }
                else
                {
                    Console.Write(".");
                }

                // Wait for a specified delay between polling attempts
                Thread.Sleep(TimeSpan.FromSeconds(delayInSeconds));
            }

            return "FAILED"; // Return FAILED if the crawler does not reach a terminal state within the maximum number of attempts
        }


        static async Task<string> ExecuteAthenaQuery(IAmazonAthena client, string query, string outputLocation)
        {
            Console.WriteLine("Executing Query:");
            Console.WriteLine(query);

            var startQueryExecutionRequest = new StartQueryExecutionRequest
            {
                QueryString = query,
                QueryExecutionContext = new QueryExecutionContext { Database = "coveragecalculator" }, 
                ResultConfiguration = new ResultConfiguration
                {
                    OutputLocation = outputLocation
                }
            };

            try
            {
                var startQueryExecutionResponse = await client.StartQueryExecutionAsync(startQueryExecutionRequest);
                return startQueryExecutionResponse.QueryExecutionId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing query: {ex.Message}");
                return null;
            }
        }

        static async Task<string> PollQueryExecutionStatus(IAmazonAthena client, string queryExecutionId, int maxAttempts = 30, int delayInSeconds = 10)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var request = new GetQueryExecutionRequest { QueryExecutionId = queryExecutionId };
                var response = await client.GetQueryExecutionAsync(request);
                var status = response.QueryExecution.Status.State;

                if (status == QueryExecutionState.SUCCEEDED || status == QueryExecutionState.FAILED || status == QueryExecutionState.CANCELLED)
                {
                    return status;
                }

                // Wait for a specified delay between polling attempts
                Thread.Sleep(TimeSpan.FromSeconds(delayInSeconds));
            }

            return "FAILED"; // Return FAILED if the query does not reach a terminal state within the maximum number of attempts
        }

        public static async Task<string> DownloadCsvFromS3Async(IAmazonS3 s3Client , string bucketName, string fileKey)
        {
            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = fileKey
            };

            using (var response = await s3Client.GetObjectAsync(request))
            using (var responseStream = response.ResponseStream)
            using (var reader = new StreamReader(responseStream))
            {
                string content = reader.ReadToEnd();
                return content;
            }
        }

        public static List<T> ParseCsvContent<T>(string csvContent) 
        {
            var records = new List<T>();
            
            using (var reader = new StringReader(csvContent))
            {
                var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    // Ignore header validation errors
                    HeaderValidated = null,
                    MissingFieldFound = null, // Ignore missing fields
                    //BadDataFound = null // Optionally, handle bad data issues
                };

                using (var csv = new CsvReader(reader, csvConfig))
                {
                    records = csv.GetRecords<T>().ToList();
                }
            }

            return records;
        }


        static void GenerateShapefileFromAggregates(List<CoverageAggregate> resultSet, string shapeFilePath)
        {
            //loop over teh resultSet and create a feature for each of the entry in the resultset with the appropriate attributes

            var features = new List<Feature>();

            foreach(var entry in resultSet.Where(result => result.IsValid))
            {
                var index = new H3Index(entry.H3Index);

                var polygon = index.GetCellBoundary();

                var parentAttributes = new AttributesTable
                {
                    {"index", entry.H3Index},
                    {"resolution", index.Resolution},
                    {"sampleCng", entry.SampleCount},
                    {"avgNgwRcv", entry.AvgNumberOfGatewayReceivingUplink},
                    {"avgMaxDst", entry.AvgMaxDistance ?? 0},
                    {"avgDst", entry.AvgDistance ?? 0},
                    {"avgMinDst", entry.AvgMinDistance ?? 0},
                    {"avgMaxRssi", entry.AvgMaxRssi},
                    {"avgRssi", entry.AvgRssi},
                    {"avgMinRssi", entry.AvgMinRssi},
                    {"avgMaxSnr", entry.AvgMaxSnr},
                    {"avgSnr", entry.AvgSnr},
                    {"avgMinSnr", entry.AvgMinSnr}
                };                

                var feature = new Feature(polygon, parentAttributes);
                features.Add(feature);
            }

            if ( features.Count > 0)
            {
                string shapeFileName = $"{shapeFilePath}.shp";

                Shapefile.WriteAllFeatures(features, shapeFileName);

                Console.WriteLine("Coverage Map ShapeFile created"); 
            }
            else
            {
                Console.WriteLine("No Feature to be stored in ShapeFile, can't create it!");
            }    
        }
    
        static void GenerateFilteringAreaShapefile(IEnumerable<H3Index> areas, string shapeFilePath)
        {
            var features = new List<Feature>();

            foreach(var area in areas)
            {
                var polygon = area.GetCellBoundary();

                var parentAttributes = new AttributesTable
                {
                    {"index", area.ToString() },
                    {"resolution", area.Resolution},
                };                

                var feature = new Feature(polygon, parentAttributes);
                features.Add(feature);
            }

            string shapeFileName = $"{shapeFilePath}.shp";

            Shapefile.WriteAllFeatures(features, shapeFileName);

            Console.WriteLine("Filtering Area ShapeFile created"); 
        }
    
    }


    public class CoverageAggregate
    {
        public string H3Index { get; set; }
        public int SampleCount { get; set; }
        public float? AvgNumberOfGatewayReceivingUplink { get; set; }
        public float? AvgMaxDistance { get; set; }
        public float? AvgDistance { get; set; }
        public float? AvgMinDistance { get; set; }
        public float? AvgMaxRssi { get; set; }
        public float? AvgRssi { get; set; }
        public float? AvgMinRssi { get; set; }
        public float? AvgMaxSnr { get; set; }
        public float? AvgSnr { get; set; }
        public float? AvgMinSnr { get; set; }

        public bool IsValid { get
            {
                if ( AvgMaxRssi.HasValue && AvgRssi.HasValue && AvgMinRssi.HasValue &&
                    AvgMaxSnr.HasValue && AvgSnr.HasValue && AvgMinSnr.HasValue)            
                    return true;            
                else
                    return false;
            }
        }
    }
}
