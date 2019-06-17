﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CommandLine;
using Maploader.Renderer;
using Maploader.Renderer.Imaging;
using Maploader.Renderer.Texture;
using Maploader.World;
using PapyrusCs.Database;
using PapyrusCs.Strategies;
using PapyrusCs.Strategies.Dataflow;
using PapyrusCs.Strategies.For;

namespace PapyrusCs
{
    class Program
    {
        private static int _totalChunksRendered = 0;
        private static int _totalChunk = 0;
        private static Stopwatch _time;
        private static Stopwatch _time2 = new Stopwatch();



        static int Main(string[] args)
        {
            _time = Stopwatch.StartNew();

            var options = new Options();

            Parser.Default.ParseArguments<Options>(args).WithParsed(o =>
            {
                options = o;
                options.Loaded = true;

            });

            if (!options.Loaded)
            {
                return -1;
            }

            // Parameter Validation
            try
            {
                if (options.LimitX != null)
                {
                    var splittedLimit = options.LimitX.Split(',').Select(x => Convert.ToInt32(x)).OrderBy(x => x).ToArray();
                    if (splittedLimit.Length != 2)
                        throw new ArgumentOutOfRangeException("LimitX");
                    options.LimitXLow = splittedLimit[0];
                    options.LimitXHigh = splittedLimit[1];
                }
            }
            catch (Exception)
            {
                Console.WriteLine($"The value '{options.LimitX}' for the LimitZ parameter is not valid. Try something like -10,10");
                return -1;
            }

            try
            {
                if (options.LimitZ != null)
                {
                    var splittedLimit = options.LimitZ.Split(',').Select(x => Convert.ToInt32(x)).OrderBy(x => x).ToArray();
                    if (splittedLimit.Length != 2)
                        throw new ArgumentOutOfRangeException("LimitZ");
                    options.LimitZLow = splittedLimit[0];
                    options.LimitZHigh = splittedLimit[1];
                }
            }
            catch (Exception)
            {
                Console.WriteLine($"The value '{options.LimitZ}' for the LimitZ parameter is not valid. Try something like -10,10");
                return -1;
            }

            options.FileFormat = options.FileFormat.ToLowerInvariant();
            if (new string[] {"jpg", "png", "webp"}.All(x => x != options.FileFormat))
            {
                Console.WriteLine($"The value {options.FileFormat} is not allowed for option -f");
                return -1;
            }

            var world = new World();
            try
            {
                Console.WriteLine("Opening world...");
                world.Open(options.MinecraftWorld);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not open world at '{options.MinecraftWorld}'!. Did you specify the .../db folder?");
                Console.WriteLine("The reason was:");
                Console.WriteLine(ex.Message);
                return -1;
            }

            // Start Generation
            int xmin = 0;
            int xmax = 0;
            int zmin = 0;
            int zmax = 0;
            HashSet<LevelDbWorldKey2> allSubChunks = null;

            Func<LevelDbWorldKey2, bool> constraintX = b => true;
            Func<LevelDbWorldKey2, bool> constraintZ = b => true;

            if (options.LimitXLow.HasValue && options.LimitXHigh.HasValue)
            {
                xmin = options.LimitXLow.Value;
                xmax = options.LimitXHigh.Value;
                Console.WriteLine($"Limiting X to {xmin} to {xmax}");
                var xmax1 = xmax;
                var xmin1 = xmin;
                constraintX = key => key.X >= xmin1 && key.X <= xmax1;
            }

            if (options.LimitZLow.HasValue && options.LimitZHigh.HasValue)
            {
                zmin = options.LimitZLow.Value;
                zmax = options.LimitZHigh.Value;
                Console.WriteLine($"Limiting Z to {zmin} to {zmax}");
                var zmax1 = zmax;
                var zmin1 = zmin;
                constraintZ = key => key.Z >= zmin1 && key.Z <= zmax1;
            }

            if (options.Dimension == 1) // Nether 
            { 
                options.TrimCeiling = true;
                if (options.LimitY == -1)
                {
                    options.LimitY = 120;
                }
            }
            
            Console.WriteLine("Generating a list of all chunk keys in the database.\nThis could take a few minutes");
            var keys = world.GetDimension(options.Dimension).ToList();
            allSubChunks = keys.Select(x => new LevelDbWorldKey2(x))
                .Where(k => constraintX(k) && constraintZ(k))
                .ToHashSet();

            _totalChunk = allSubChunks.GroupBy(x => x.XZ).Count();
            Console.WriteLine($"Total Chunk count {_totalChunk}");
            Console.WriteLine();

            xmin = allSubChunks.Min(x => x.X);
            xmax = allSubChunks.Max(x => x.X);
            zmin = allSubChunks.Min(x => x.Z);
            zmax = allSubChunks.Max(x => x.Z);

            Console.WriteLine($"The total dimensions of the map are");
            Console.WriteLine($"  X: {xmin} to {xmax}");
            Console.WriteLine($"  Z: {zmin} to {zmax}");
            Console.WriteLine();

            if (options.LimitY > 0)
            {
                Console.WriteLine($"Limiting Y to {options.LimitY}");
            }

            const int chunkSize = 256;
            int chunksPerDimension = 2;
            int tileSize = chunkSize * chunksPerDimension;
            Directory.CreateDirectory(options.OutputPath);

            // db stuff
            var textures = ReadTerrainTextureJson();
            var zoom = CalculateZoom(xmax, xmin, zmax, zmin, chunksPerDimension, out var extendedDia);

            var strat = InstanciateStrategy(options);
            ConfigureStrategy(strat,  options, allSubChunks, extendedDia, zoom, world, textures, tileSize, chunksPerDimension, chunkSize, zmin, zmax, xmin, xmax);

            strat.Init();

            // other stuff
            
            strat.RenderInitialLevel();

            var missingTextures = strat.MissingTextures;
            if (missingTextures != null)
            {
                File.WriteAllLines("missingtextures.txt", missingTextures.Distinct());
            }


            Console.WriteLine("Time is {0}", _time.Elapsed);
            strat.RenderZoomLevels();


            WriteMapHtml(tileSize, options.OutputPath, options.MapHtml, strat.GetSettings(), strat.IsUpdate);

            strat.Finish();
            Console.WriteLine("Total Time {0}", _time.Elapsed);
            world.Close();


            Console.WriteLine("Map generation finished!");
            return 0;
        }

        private static IRenderStrategy InstanciateStrategy(Options options)
        {
            IRenderStrategy strat = null;
            switch (options.Strategy)
            {
                case Strategy.ParallelFor:
                    strat = new ParallelForRenderStrategy<Bitmap>(new SystemDrawing());
                    break;
                case Strategy.SingleFor:
                    strat = new SingleForRenderStrategy<Bitmap>(new SystemDrawing());
                    break;
                case Strategy.Dataflow:
                    strat = new DataFlowStrategy<Bitmap>(new SystemDrawing());
                    break;
                default:
                    strat = new SingleForRenderStrategy<Bitmap>(new SystemDrawing());
                    break;
            }

            return strat;
        }

        private static void ConfigureStrategy(IRenderStrategy strat, Options options,
            HashSet<LevelDbWorldKey2> allSubChunks,
            int extendedDia, int zoom, World world, Dictionary<string, Texture> textures, int tileSize,
            int chunksPerDimension, int chunkSize,
            int zmin, int zmax, int xmin, int xmax)
        {
            strat.RenderSettings = new RenderSettings()
            {
                RenderCoordinateStrings = options.RenderCoords,
                RenderMode = options.RenderMode,
                MaxNumberOfThreads = options.MaxNumberOfThreads,
                YMax = options.LimitY,
                BrillouinJ = options.BrillouinJ,
                BrillouinDivider = options.BrillouinDivider,
                TrimCeiling = options.TrimCeiling,
            };
            strat.AllWorldKeys = allSubChunks;
            strat.InitialDiameter = extendedDia;
            strat.InitialZoomLevel = (int)zoom;
            strat.World = world;
            strat.TotalChunkCount = _totalChunk;
            strat.TexturePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "textures");
            strat.TextureDictionary = textures;
            strat.OutputPath = options.OutputPath;
            strat.TileSize = tileSize;
            strat.ChunksPerDimension = chunksPerDimension;
            strat.ChunkSize = chunkSize;
            strat.ZMin = zmin;
            strat.ZMax = zmax;
            strat.XMin = xmin;
            strat.XMax = xmax;
            strat.ChunksRendered += RenderDisplay;
            strat.ZoomLevelRenderd += RenderZoom;
            strat.FileFormat = options.FileFormat;
            strat.FileQuality = options.Quality;
            strat.Dimension = options.Dimension;
            strat.DeleteExistingUpdateFolder = options.DeleteExistingUpdateFolder;
        }

        private static void WriteMapHtml(int tileSize, string outputPath, string mapHtmlFile, Settings[] settings,
            bool isUpdate)
        {
            try
            {
                var layernames = new string[] { "Overworld", "Nether", "End" };
                var mapHtmlContext = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "map.thtml"));

                var layertemplate = "var mapdata%dim% = L.tileLayer('./%path%/{z}/{x}/{y}.%fileformat%', { \n" +
                    "attribution: '<a href=\"https://github.com/mjungnickel18/papyruscs\">PapyrusCS</a>',\n" +
                    "minNativeZoom: %minnativezoom%,\n" +
                    "minZoom: %minzoom%,\n" +
                    "maxNativeZoom: %maxnativezoom%,\n"+
                    "maxZoom: %maxzoom%,\n"+
                    "noWrap: true,\n"+
                    "tileSize: %tilesize%,\n"+
                "});";

                var minZoom = settings.Select(x => x.MinZoom).Min();

                StringBuilder sb = new StringBuilder();
                foreach (var setting in settings)
                {
                    var newlayer = layertemplate;

                    newlayer = newlayer.Replace("%maxnativezoom%", setting.MaxZoom.ToString());
                    newlayer = newlayer.Replace("%maxzoom%", (setting.MaxZoom + 2).ToString());
                    newlayer = newlayer.Replace("%minnativezoom%", (setting.MinZoom).ToString());
                    newlayer = newlayer.Replace("%minzoom%", (minZoom).ToString());
                    newlayer = newlayer.Replace("%tilesize%", (tileSize).ToString());
                    newlayer = newlayer.Replace("%fileformat%", setting.Format);
                    newlayer = newlayer.Replace("%path%", $"dim{setting.Dimension}");
                    newlayer = newlayer.Replace("%dim%", setting.Dimension.ToString());

                    sb.AppendLine(newlayer);
                }

                sb.AppendLine("var baseMaps = {");
                foreach (var s in settings)
                {
                    var dimName = layernames.Length > s.Dimension ? layernames[s.Dimension] : $"Dimension{s.Dimension}";
                    sb.AppendFormat("\"{0}\": mapdata{1},", dimName, s.Dimension);
                }
                sb.AppendLine("}");

                sb.AppendLine($"L.control.layers(baseMaps).addTo(map);");

                sb.AppendFormat("mapdata{0}.addTo(map);", settings.Min(x => x.Dimension));


                mapHtmlContext = mapHtmlContext.Replace("%layers%", sb.ToString());
                mapHtmlContext = mapHtmlContext.Replace("%globalminzoom%", settings.First(x => x.Dimension == settings.Min(y=>y.Dimension)).MinZoom.ToString());
                mapHtmlContext = mapHtmlContext.Replace("%factor%", (Math.Pow(2, settings.First().MaxZoom - 4)).ToString(CultureInfo.InvariantCulture));


                File.WriteAllText(Path.Combine(outputPath, "map", mapHtmlFile), mapHtmlContext);
                if (isUpdate)
                {
                    File.WriteAllText(Path.Combine(outputPath, "update", mapHtmlFile), mapHtmlContext);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not write map.html");
                Console.WriteLine(ex.Message);
            }
        }

        private static int CalculateZoom(int xmax, int xmin, int zmax, int zmin, int chunksPerDimension, out int extendedDia)
        {
            if (xmin > 0 && xmax > 0)
                xmin = 0;

            if (zmin > 0 && zmax > 0)
                zmin = 0;

            if (xmin < 0 && xmax < 0)
                xmax = 0;

            if (zmin < 0 && zmax < 0)
                zmax = 0;

            var maxDiameter = Math.Max(Math.Abs(xmax - xmin + 1), Math.Abs(zmax - zmin + 1));
            Console.WriteLine($"The maximum diameter of the map is {maxDiameter}");

            maxDiameter = (maxDiameter + (chunksPerDimension - 1)) / chunksPerDimension;
            Console.WriteLine($"For {chunksPerDimension} chunks per tile, new max diameter is {maxDiameter}");

            var zoom = (int)(Math.Ceiling(Math.Log(maxDiameter) / Math.Log(2)));
            extendedDia = (int)Math.Pow(2, zoom);

            Console.WriteLine($"To generate the zoom levels, we expand the diameter to {extendedDia}");
            Console.WriteLine($"This results in {zoom + 1} zoom levels");
            return zoom;
        }

        private static Dictionary<string, Texture> ReadTerrainTextureJson()
        {
            Console.WriteLine("Reading terrain_texture.json...");
            var json = File.ReadAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"textures", "terrain_texture.json"));
            var ts = new TerrainTextureJsonParser(json, "");
            var textures = ts.Textures;
            Console.WriteLine();
            return textures;
        }

        private static void RenderZoom(object sender, ZoomRenderedEventArgs e)
        {
            Console.Write($"\r{e.LinesRendered} of {e.TotalLines} lines render @ zoom level {e.ZoomLevel}      ");
        }


        private static void RenderDisplay(object sender, ChunksRenderedEventArgs e)
        {
            if (!_time2.IsRunning)
                _time2.Start();
            Interlocked.Add(ref _totalChunksRendered, e.RenderedChunks);
            Console.Write($"\r{_totalChunksRendered} of {_totalChunk} Chunks render @ {(_totalChunksRendered) / _time2.Elapsed.TotalSeconds:0.0} cps     ");
        }


    }
}

