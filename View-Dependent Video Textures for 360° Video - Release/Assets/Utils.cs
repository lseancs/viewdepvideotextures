using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;

namespace RenderHeads.Media.AVProVideo
{
    [Serializable]
    public class CostRow
    {
        public float[] row_costs = null;

        public CostRow()
        {
        }

        public CostRow(int length)
        {
            row_costs = new float[length];
        }

        public void Set(int i, float val)
        {
            row_costs[i] = val;
        }

        public float Get(int i)
        {
            return row_costs[i];
        }

        public float[] Get()
        {
            return row_costs;
        }

        public int Length()
        {
            if (row_costs == null)
            {
                return 0;
            }
            return row_costs.Length;
        }

        public override string ToString()
        {
            return "COSTROW: " + string.Join(",", row_costs);
        }
    }

    [Serializable]
    public class Block
    {
        public int start;  // inclusive
        public int end;  // exclusive
        public bool postProcessWarning = false;  // Post-process heuristic has been used.
        public bool penalized = false;  // Post-process heuristic picked arc that does not satisfy all user-set thresholds.

        public Block()
        {
        }

        public Block(int startFrame, int endFrame)
        {
            start = startFrame;
            end = endFrame;
        }

        public override string ToString()
        {
            return "(" + start + ", " + end + ") ";
        }

    }

    [Serializable]
    public class View
    {
        public List<LoopArc> loopArcs = new List<LoopArc>();
        
        [XmlIgnore]
        private LoopArc[] loopArcsArr;
        private bool loopArcsDirty = false;

        public string cost_matrix_file;

        public int currentLoopIndex = -1;

        public CostRow[] cost_matrix = null;


        [XmlIgnore]
        // Key: Perceptual Threshold (str), min loop length (in frames).
        // Value: array of ints representing the best frame to jump to from each frame, 
        // i.e., frame with lowest perceptual cost that satisfies that user-defined minimum loop length and perceptual threshold. If non-existent, then -1.
        public Dictionary<Tuple<string, int>, int[]> bestToFrames = new Dictionary<Tuple<string, int>, int[]>();

        public View()
        {
        }

        public LoopArc[] GetLoopArcs()
        {
            if (loopArcsDirty || loopArcsArr == null || loopArcsArr.Length != loopArcs.Count)
            {
                loopArcsArr = loopArcs.ToArray();
                Array.Sort(loopArcsArr);
                loopArcsDirty = false;
            }
            return loopArcsArr;
        }

        public int MatchExistingLoop(LoopArc arc)
        {
            for (int j = 0; j < loopArcs.Count; j++)
            {
                if (loopArcs[j].Equals(arc))
                {
                    return j;
                }
            }
            return -1;
        }

        public bool CanJumpTo(int currentFrame, int toFrame, float threshold)
        {
            if (cost_matrix == null )
            {
                return false;
            }
            float costToJump = cost_matrix[currentFrame].Get(toFrame);
            bool canJump = costToJump <= threshold;
            Debug.Log("Current frame: " + currentFrame + ". ToFrame: " + toFrame + ". Cost: " + costToJump + ". THreshold: " + threshold + ". Can jump? " + canJump);
            return canJump;
        }

        public LoopArc GetCurrentLoop()
        {
            if (currentLoopIndex < 0)
            {
                return null;
            }
            return loopArcs[currentLoopIndex];
        }

        public bool MatchCurrentLoop(LoopArc arc, float time)
        {
            if (currentLoopIndex < 0)
                return false;
            return arc.Equals(loopArcs[currentLoopIndex]) && (time - loopArcs[currentLoopIndex].toTime) <= HeadTrack._LOOP_TRANS_DURATION;
        }

        public void AddLoopArc(float fromTime, float toTime, float clipStart, float clipEnd)
        {
            if (loopArcs == null)
            {
                loopArcs = new List<LoopArc>();
            }
            LoopArc arc = new LoopArc(fromTime, toTime, clipStart, clipEnd);
            int existingLoopIndex = MatchExistingLoop(arc);
            if (existingLoopIndex < 0)
            {
                loopArcs.Add(arc);
                currentLoopIndex = loopArcs.Count - 1;
            }
            else
            {
                currentLoopIndex = existingLoopIndex;
            }
        }

        public int GetRowLength(int frame)
        {
            if (cost_matrix == null || frame >= cost_matrix.Length || cost_matrix[frame] == null)
            {
                return 0;
            }
            return cost_matrix[frame].Length();
        }

        public float[] GetCostsFrom(int frame)
        {
            if (frame >= this.cost_matrix.Length)
            {
                Debug.Log("Got bad request: " + frame + ". Total length: " + this.cost_matrix.Length);
            }
            return (float[])this.cost_matrix[frame].Get().Clone();
        }

        public Tuple<float, float> ReadCostMatrixFile(string newCostMatrixFile)
        {
            cost_matrix_file = newCostMatrixFile;

            string[] cost_matrix_str = null;
            int rows = 0;
            int cols = 0;

            XmlDocument doc = new XmlDocument();
            string actualPath = Path.Combine(Application.dataPath, "StreamingAssets", "Editor", cost_matrix_file);
            doc.Load(actualPath);
            float minCost = float.MaxValue;
            float maxCost = float.MinValue;
            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.Name == "filtered_costs")
                {
                    string data = node["data"].InnerText.Trim();
                    cost_matrix_str = data.Split(new char[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    rows = int.Parse(node["rows"].InnerText);
                    cols = int.Parse(node["cols"].InnerText);
                    Debug.Assert(cost_matrix_str.Length == rows * cols);

                    cost_matrix = new CostRow[rows];
                    
                    for (int i = 0; i < cost_matrix_str.Length; i++)
                    {
                        float cost = float.Parse(cost_matrix_str[i], NumberStyles.Float);

                        minCost = Mathf.Min(cost, minCost);
                        maxCost = Mathf.Max(cost, maxCost);

                        int row_n = (int)(i / cols);
                        int col_n = i % cols;

                        if (cost_matrix[row_n] == null)
                        {
                            cost_matrix[row_n] = new CostRow(cols);
                        }
                        cost_matrix[row_n].Set(col_n, cost);
                    }

                    for (int i = 0; i < rows; i++)
                    {
                        Debug.Assert(cost_matrix[i].Length() == cols);
                    }
                }
            }
            doc = null;
            return Tuple.Create(minCost, maxCost);
        }
    }

    [Serializable]
    public class BlockFrames
    {
        public List<Block> blocks = new List<Block>();
        public BlockFrames()
        {
        }

        public override string ToString()
        {
            return string.Join(",", blocks);
        }

        public bool insideABlock(int frame)
        {
            foreach(Block block in blocks)
            {
                if (frame < block.end && frame >= block.start)
                {
                    return true;
                }
            }
            return false;
        }
    }

    [Serializable]
    public class ListOfFrames
    {
        public List<int> frames = new List<int>();
        public ListOfFrames()
        {

        }
    }

    [Serializable]
    public class ListOfCosts
    {
        public List<float> costs = new List<float>();
        public ListOfCosts()
        {

        }
    }

    [Serializable]
    public class EdgeCostRow
    {
        public float[] row;
        public EdgeCostRow()
        {

        }
    }

    [Serializable]
    public class StoryPoint
    {
        public static float DEFAULT_PERCEPTUAL_THRES = 2000f;
        public static int NUM_VIEWS = 40;  // Should be the same for all story points.
        public static float VIEW_HFOV = 80.65347f / 180f * Mathf.PI; //  FOV in radians.
        public static float FACE_VFOV = 180f / 180f * Mathf.PI; //  FOV in radians.
        public float targetX;
        public float targetY;
        public float targetWidth = 1f;  // Default one pixel wide and tall.
        public float targetHeight = 1f;
        public float gateTime = -1f;
        public bool lookAt;  // Does viewer have to look at or away from the target pixel to move on?
        public float startTime;
        public float endTime;
        public string LoopsFolder;
        public bool jumpImmediately;
        public bool crossFadeAudio = true;
        public float jumpThreshold;

        [XmlIgnore]
        public List<LoopArc> loopArcs = new List<LoopArc>();  // Times from which we can jump seamlessly to another part of the clip.
        private Vector3[] gateTimePoints;
        private Rect gateTimeRect;

        public float minLoopLengthSec;
        public float perceptualThreshold;
        public string cutFile = "";
        public string allArcsFile = "";
        public string validArcFile = "";
        public string extraCostsFile = "";
        public string edgeCostMatrixFile = "";

        [SerializeField, XmlIgnore]
        public BlockFrames[] cutFrameBlocks;
        [SerializeField, XmlIgnore]
        public List<ListOfFrames> cut;
        [SerializeField, XmlIgnore]
        public List<ListOfCosts> extraCosts;
        [SerializeField, XmlIgnore]
        public List<ListOfFrames> validArcs;
        [SerializeField, XmlIgnore]
        public List<ListOfFrames> allArcs;
        [SerializeField, XmlIgnore]
        public List<EdgeCostRow> edgeCosts;
        [SerializeField, XmlIgnore]
        // Blocks of valid frames (used for quickly visualizing valid frames that satisfy user-defined thresholds, i.e. min loop length + perceptual threshold.
        public BlockFrames[] validFrameBlocks; 

        [XmlIgnore]
        public GameObject _Sphere;

        [XmlIgnore]
        public View[] views = null;

        [XmlIgnore]
        public GatedClip _component = null;

        public static StoryPoint ReadFromXML(XmlNode node, GameObject sphere)
        {
            float targetX = float.Parse(node["targetX"].InnerText, CultureInfo.InvariantCulture.NumberFormat);
            float targetY = float.Parse(node["targetY"].InnerText, CultureInfo.InvariantCulture.NumberFormat);
            float targetWidth = node["targetWidth"] != null ? float.Parse(node["targetWidth"].InnerText, CultureInfo.InvariantCulture.NumberFormat) : 1f;
            float targetHeight = node["targetHeight"] != null ? float.Parse(node["targetHeight"].InnerText, CultureInfo.InvariantCulture.NumberFormat) : 1f;
            float gateTime = node["gateTime"] != null ? float.Parse(node["gateTime"].InnerText, CultureInfo.InvariantCulture.NumberFormat) : -1f;
            bool lookAt = bool.Parse(node["lookAt"].InnerText);
            float startTime = float.Parse(node["startTime"].InnerText, CultureInfo.InvariantCulture.NumberFormat);
            float endTime = float.Parse(node["endTime"].InnerText, CultureInfo.InvariantCulture.NumberFormat);
            float jumpThreshold = float.Parse(node["jumpThreshold"].InnerText, CultureInfo.InvariantCulture.NumberFormat);
            string loopsFolder = node["LoopsFolder"] != null ? node["LoopsFolder"].InnerText : "";
            bool jumpImmediately = node["jumpImmediately"] != null ? bool.Parse(node["jumpImmediately"].InnerText) : false ;
            float loopLength = node["minLoopLengthSec"] != null ? float.Parse(node["minLoopLengthSec"].InnerText, CultureInfo.InvariantCulture.NumberFormat) : 1f;
            float perceptualThreshold = node["perceptualThreshold"] != null ? float.Parse(node["perceptualThreshold"].InnerText, CultureInfo.InvariantCulture.NumberFormat) : StoryPoint.DEFAULT_PERCEPTUAL_THRES;
            string edgeFile = node["edgeCostMatrixFile"] != null ? node["edgeCostMatrixFile"].InnerText : "";
            string cutFile = node["cutFile"] != null ? node["cutFile"].InnerText : "";
            string allArcsFile = node["allArcsFile"] != null ? node["allArcsFile"].InnerText : "";
            string validArcFile = node["validArcFile"] != null ? node["validArcFile"].InnerText : "";
            string extraCostsFile = node["extraCostsFile"] != null ? node["extraCostsFile"].InnerText : "";
            bool muteAudio = node["muteAudio"] != null ? bool.Parse(node["muteAudio"].InnerText) : false;
            bool crossFadeAudio = node["crossFadeAudio"] != null ? bool.Parse(node["crossFadeAudio"].InnerText) : true;

            return new StoryPoint(targetX, targetY, targetWidth, targetHeight, gateTime, lookAt, startTime, endTime, jumpImmediately, jumpThreshold, loopLength, perceptualThreshold, loopsFolder, sphere, edgeFile, cutFile, allArcsFile, validArcFile, extraCostsFile, crossFadeAudio);
        }

        public StoryPoint()
        {
        }

        public StoryPoint(float newTargetX, float newTargetY, float newTargetWidth, float newTargetHeight, float newGateTime, bool newLookAt, float newStart, float newEnd, bool newJumpImmed, float newJump, float loopLength, float newPerceptualThreshold, string loopsFolder = "", GameObject sphere=null, string newEdgeFile = "", string newCutFile = "", string newAllArcsFile = "", string newValidArcsFile = "", string newExtraCostsFile="", bool newCrossFadeAudio=true)
        {
            targetX = newTargetX;
            targetY = newTargetY;
            targetWidth = newTargetWidth;
            targetHeight = newTargetHeight;
            gateTime = newGateTime;
            lookAt = newLookAt;
            startTime = newStart;
            endTime = newEnd;
            jumpThreshold = newJump;
            jumpImmediately = newJumpImmed;
            minLoopLengthSec = loopLength;
            perceptualThreshold = newPerceptualThreshold;
            _Sphere = sphere;
            crossFadeAudio = newCrossFadeAudio;

            this.ReadEdgeCostFile(newEdgeFile);
            this.ReadAllArcsFile(newAllArcsFile);
            this.ReadValidArcFile(newValidArcsFile);
            this.ReadExtraCostsFile(newExtraCostsFile);
            this.ReadCutFile(newCutFile);
            
            LoopsFolder = loopsFolder;
            InitializeViews();
        }

        public override string ToString()
        {
            return "Storypoint: " + startTime + " to " + endTime + ". GatedClip: " + this._component;
        }

        public Rect GetTargetRect()
        {
            return new Rect(targetX, targetY, targetWidth, targetHeight);
        }

        public void ComputeCutTimeBlocks(int viewingIndex)
        {
            cutFrameBlocks[viewingIndex] = new BlockFrames();

            List<int> currentCut = cut[viewingIndex].frames;
            currentCut.Sort();
            for (int c = 0; c < currentCut.Count; c++)
            {
                Block block = new Block();
                block.start = currentCut[c];
                block.end = currentCut[c]+1;
                block.postProcessWarning = (allArcs[viewingIndex].frames[currentCut[c]] != validArcs[viewingIndex].frames[c]);  // Used post-process heuristic.
                block.penalized = extraCosts[viewingIndex].costs[c] > 0;  // After applying post-process heuristic, introduced extra cost.
                cutFrameBlocks[viewingIndex].blocks.Add(block);
            }
        }

        public List<Block> GetCutTimeBlock(int viewingIndex)  // Position of clip on timeline.
        {
            if (cutFile == ""|| cutFrameBlocks == null)
            {
                return null;
            }
            return cutFrameBlocks[viewingIndex].blocks;
        }

        public Vector3[] GetGateTimePoints(Rect rect, float PIXELS_PER_SEC)  // Position of clip on timeline.
        {
            if (gateTime < 0f)
            {
                return null;
            }
            //Debug.Log("Gate timeline pos: " + gateTimelinePos);
            if (gateTimePoints == null || !gateTimeRect.Equals(rect))
            {
                gateTimeRect = rect;
                var startPoint = new Vector2(rect.x + gateTime * PIXELS_PER_SEC, rect.y);
                var endPoint = new Vector2(rect.x + gateTime * PIXELS_PER_SEC, rect.yMax);

                List<Vector3> points = new List<Vector3>();
                for (float t = 0f; t < 1f; t += 0.05f)
                {
                    var result = Vector2.Lerp(startPoint, endPoint, t);
                    points.Add(new Vector3(result.x, result.y, 0));
                }
                points.Add(new Vector3(endPoint.x, endPoint.y, 0));
                gateTimePoints = points.ToArray();
            }
            return gateTimePoints;
        }

        public void InitializeViews()
        {
            views = new View[NUM_VIEWS];  // Resetting views.
            for (int i = 0; i < views.Length; i++)
            {
                views[i] = new View();
            }
            if (LoopsFolder != "" && Directory.Exists(LoopsFolder))
            {
                string[] costMatrixFiles = Directory.GetFiles(LoopsFolder);
                foreach (string costMatrixFile in costMatrixFiles)
                {
                    if (costMatrixFile.EndsWith("_cost_matrices.xml"))
                    {
                        string resultString = Regex.Match(Path.GetFileName(costMatrixFile), @"\d+").Value;
                        int viewNumber = Int32.Parse(resultString);
                        if (viewNumber < views.Length)
                        {
                            string parent = Directory.GetParent(costMatrixFile).ToString();
                            string costMatrixLeaf = Path.GetFileName(costMatrixFile);
                            string subpath = Path.Combine(parent, costMatrixLeaf);
                            views[viewNumber].ReadCostMatrixFile(subpath);
                        }
                    }
                }
                PopulateBackwardArcs();  // Add loop arcs for rendering on timeline.
            }
        }

        public int MinLoopLengthFrames()
        {
            HeadTrack headtrack = _Sphere.GetComponent<HeadTrack>();
            int frames = (int)Math.Round(minLoopLengthSec * headtrack.GetVideoFps());
            return frames;
        }

        public Tuple<string, int> GetBlockKey()
        {
            return Tuple.Create<string, int>(perceptualThreshold.ToString("0.000"), MinLoopLengthFrames());
        }

        public void FindGrayFramesForView(int i)
        {
            int totalFrames = views[i].GetRowLength(0);
            if (!views[i].bestToFrames.ContainsKey(GetBlockKey()))
            {
                // Compute bestToFrames.
                int[] bestToFrames = new int[totalFrames];
                for (int row = 0; row < totalFrames; row++)
                {
                    float minCost = float.MaxValue;
                    int arc = -1;
                    for (int c = 0; c <= row - MinLoopLengthFrames(); c++)
                    {
                        // Find lowest-cost arc from frame row that satisfies user-set min loop length and perceptual threshold.
                        if (views[i].cost_matrix[row].Get(c) < minCost && views[i].cost_matrix[row].Get(c) <= perceptualThreshold)
                        {
                            minCost = views[i].cost_matrix[row].Get(c);
                            arc = c;
                        }
                    }
                    bestToFrames[row] = arc;
                }
                views[i].bestToFrames.Add(GetBlockKey(), bestToFrames);
            }
            ComputeBlockValidFrames(i);
        }

        public bool HasCostMatrices()
        {
            bool allCostMatricesLoaded = true;
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i].cost_matrix == null || views[i].cost_matrix.Length == 0)
                {
                    allCostMatricesLoaded = false;
                }
            }
            return allCostMatricesLoaded;
        }

        public void ComputeAllGrayFrames()
        {
            if (validFrameBlocks == null || validFrameBlocks.Length == 0)
                validFrameBlocks = new BlockFrames[this.views.Length];
            for (int i = 0; i < views.Length; i++)
            {
                FindGrayFramesForView(i);
            }
        }

        public bool ReachedCutFrame(int view_num, int frame_num)
        {
            if (cut.Count == 0)
            {
                return false;
            }
            ListOfFrames cutFrames = cut[view_num];
            return cutFrames.frames.Contains(frame_num);
        }

        public bool PastLastCutFrame(int view_num, int frame_num)
        {
            if (cut.Count == 0)
            {
                return false;
            }
            ListOfFrames cutFrames = cut[view_num];
            return frame_num > cutFrames.frames.Max();
        }

        public int FindJumpToFrame(int view_num, int frame_num)
        {
            ListOfFrames cutFrames = cut[view_num];
            int index = cutFrames.frames.FindIndex(x => x == frame_num);
            int validFrame = validArcs[view_num].frames[index];
            //Debug.Log("Cut Frames in " + view_num + ": " + string.Join(",", cutFrames.frames));
            //Debug.Log("Current frame is : " + frame_num + ". Index: " + index + ". Valid frame: " + validFrame);
            return validFrame;
        }

        public int FindLastCutFrame(int face_num)
        {
            ListOfFrames cutFrames = cut[face_num];
            return cutFrames.frames.Max();
        }

        public bool HasComponent()
        {
            return _component != null;
        }

        public void ReadEdgeCostFile(string newEdgeFile)
        {
            if (newEdgeFile == this.edgeCostMatrixFile)
            {
                return;
            }
            Debug.Log("Reading in new edge cost matrix at: " + newEdgeFile);
            this.edgeCostMatrixFile = newEdgeFile;

            XmlDocument doc = new XmlDocument();
            string[] cost_matrix_str;
            int rows, cols;
            string actualPath = Path.Combine(Application.dataPath, "StreamingAssets", "Editor", edgeCostMatrixFile);
            doc.Load(actualPath);
            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.Name == "edge_costs")
                {
                    string data = node["data"].InnerText.Trim();
                    cost_matrix_str = data.Split(new char[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    rows = int.Parse(node["rows"].InnerText);
                    cols = int.Parse(node["cols"].InnerText);
                    edgeCosts = new List<EdgeCostRow>();
                    Debug.Assert(cost_matrix_str.Length == rows * cols);

                    int index = 0;
                    for (int i = 0; i < rows; i++)
                    {
                        float[] row = new float[cols];
                        for (int j = 0; j < cols; j++)
                        {
                            float cost = float.Parse(cost_matrix_str[index], NumberStyles.Float);
                            index++;
                            row[j] = cost;
                        }
                        EdgeCostRow edgeCostRow = new EdgeCostRow();
                        edgeCostRow.row = row;
                        edgeCosts.Add(edgeCostRow);
                    }

                    for (int i = 0; i < rows; i++)
                    {
                        Debug.Assert(edgeCosts[i].row.Length == cols);
                    }
                }
            }
        }

        public void ReadAllArcsFile(string newAllArcsFile)
        {
            if (newAllArcsFile == this.allArcsFile)
            {
                return;
            }
            Debug.Log("New all arcs file: " + newAllArcsFile);
            
            List<List<int>> rawData;
            string actualPath = Path.Combine(Application.dataPath, "StreamingAssets", "Editor", newAllArcsFile);
            using (StreamReader r = new StreamReader(actualPath))
            {
                string json = r.ReadToEnd();
                rawData = JsonConvert.DeserializeObject<List<List<int>>>(json);
            }

            int count = 0;
            List<ListOfFrames> items = new List<ListOfFrames>();
            foreach (List<int> item in rawData)
            {
                ListOfFrames cutFrame = new ListOfFrames();
                cutFrame.frames = item;
                items.Add(cutFrame);
                count++;
            }
            allArcs = items;
            this.allArcsFile = newAllArcsFile;
        }

        public void ReadValidArcFile(string validArcFile)
        {
            if (validArcFile == this.validArcFile)
            {
                return;
            }
            Debug.Log("New valid arc file: " + validArcFile);
            this.validArcFile = validArcFile;

            List<List<int>> rawValidArcs;
            string actualPath = Path.Combine(Application.dataPath, "StreamingAssets", "Editor", validArcFile);
            using (StreamReader r = new StreamReader(actualPath))
            {
                string json = r.ReadToEnd();
                rawValidArcs = JsonConvert.DeserializeObject<List<List<int>>>(json);
            }

            int count = 0;
            List<ListOfFrames> items = new List<ListOfFrames>();
            foreach (List<int> item in rawValidArcs)
            {
                ListOfFrames validArcsInView = new ListOfFrames();
                validArcsInView.frames = item;
                items.Add(validArcsInView);
                count++;
            }
            validArcs = items;
        }

        public void ReadExtraCostsFile(string extraCostsFile)
        {
            if (extraCostsFile == this.extraCostsFile)
            {
                return;
            }
            Debug.Log("New extra costs file: " + extraCostsFile);
            this.extraCostsFile = extraCostsFile;
            
            List<List<float>> rawExtraCosts;
            string actualPath = Path.Combine(Application.dataPath, "StreamingAssets", "Editor", extraCostsFile);
            using (StreamReader r = new StreamReader(actualPath))
            {
                string json = r.ReadToEnd();
                rawExtraCosts = JsonConvert.DeserializeObject<List<List<float>>>(json);
            }

            int count = 0;
            List<ListOfCosts> items = new List<ListOfCosts>();
            foreach (List<float> item in rawExtraCosts)
            {
                ListOfCosts extraCostsInView = new ListOfCosts();
                extraCostsInView.costs = item;
                items.Add(extraCostsInView);
                count++;
            }
            extraCosts = items;
        }

        public void ReadCutFile(string cutFile)
        {
            if (cutFile == this.cutFile)
            {
                return;
            }
            Debug.Log("New cut file: " + cutFile);
            this.cutFile = cutFile;

            List<List<int>> rawCut;
            string actualPath = Path.Combine(Application.dataPath, "StreamingAssets", "Editor", cutFile);
            using (StreamReader r = new StreamReader(actualPath))
            {
                string json = r.ReadToEnd();
                 rawCut = JsonConvert.DeserializeObject<List<List<int>>>(json);
            }

            int count = 0;
            List<ListOfFrames> items = new List<ListOfFrames>();
            foreach (List<int> item in rawCut)
            {
                ListOfFrames cutFrame = new ListOfFrames();
                cutFrame.frames = item;
                items.Add(cutFrame);
                count++;
            }
            cut = items;

            // Condense cut frames into blocks (for efficient rendering on timeline).
            cutFrameBlocks = new BlockFrames[items.Count];
            for (int v = 0; v < items.Count; v++)
            {
                ComputeCutTimeBlocks(v);
            }
        }

        public void PopulateBackwardArcs()
        {
            if (cut != null && cutFile != null && cut.Count == NUM_VIEWS) {
                float fps = _Sphere.GetComponent<HeadTrack>().GetVideoFps();
                for (int i = 0; i < NUM_VIEWS; i++)
                {
                    for (int f = 0; f < cut[i].frames.Count; f++)
                    {
                        int jumpTo = FindJumpToFrame(i, cut[i].frames[f]);
                        float jumpToTime = 1f * jumpTo / fps;
                        views[i].AddLoopArc(startTime + cut[i].frames[f] / fps, startTime + jumpToTime, startTime, endTime);
                    }
                }
            }
        }

        public bool HasCut()
        {
            return cut != null && cut.Count > 0;
        }

        public bool IsCutFrame(int viewingDirectionIndex, int frame_num)
        {
            if (cutFrameBlocks == null || cutFrameBlocks.Length == 0)
            {
                return false;
            }
            return cutFrameBlocks[viewingDirectionIndex].insideABlock(frame_num);
        }

        public List<Block> GetValidBlocksGrayFrame(int viewingDirectionIndex)
        {
            if (!HasCostMatrices())
            {
                return null;
            }
            if (HasCostMatrices() && (validFrameBlocks==null || validFrameBlocks.Length == 0))
            {
                ComputeAllGrayFrames();
            }
            if (validFrameBlocks != null && validFrameBlocks.Length > 0)
            {
                return validFrameBlocks[viewingDirectionIndex].blocks;
            }
            else
            {
                return null;
            }
        }

        public void ComputeBlockValidFrames(int viewingDirectionIndex)
        {
            List<int> validFrames = GetValidFrames(viewingDirectionIndex);
            BlockFrames validBlockFrames = new BlockFrames();
            validBlockFrames.blocks = new List<Block>();
            Block currentBlock = new Block();
            bool insideBlock = false;
            for (int i = 0; i < validFrames.Count; i++)
            {
                if (validFrames[i] == 1)
                {
                    if (!insideBlock)
                    {
                        insideBlock = true;
                        currentBlock = new Block();
                        currentBlock.start = i;
                    }
                }
                else
                {
                    if (insideBlock)
                    {
                        insideBlock = false;
                        currentBlock.end = i;  // Up to but not including i.
                        validBlockFrames.blocks.Add(currentBlock);
                        currentBlock = null;
                    }
                }
            }
            if (insideBlock)
            {
                insideBlock = false;
                currentBlock.end = validFrames.Count;  // Up to but not including validFrames.Count.
                validBlockFrames.blocks.Add(currentBlock);
            }
            
            validFrameBlocks[viewingDirectionIndex] = validBlockFrames;
        }

        public List<int> GetValidFrames(int viewingDirectionIndex)
        {
            if (!views[viewingDirectionIndex].bestToFrames.ContainsKey(GetBlockKey())) {
                FindGrayFramesForView(viewingDirectionIndex);
            }
            int[] toFrames = views[viewingDirectionIndex].bestToFrames[GetBlockKey()];
            List<int> valid = new List<int>();
            for (int e = 0; e < toFrames.Length; e++)
            {
                int validFrame = toFrames[e] >= 0 ? 1 : 0;  // if there are no valid arcs from frame e that satisfy user-defined parameters, toFrames[e] is -1.
                valid.Add(validFrame);
            }
            return valid;
        }

        public void UpdateComponent(bool enabled, Clip clip)
        {
            if (_component != null)
            {
                _component.sp = this;
                _component.setROI(targetX, targetY, targetWidth, targetHeight);
                _component.gateTime = this.gateTime;
                _component.lookAt = this.lookAt;
                _component.minLoopLengthSec = this.minLoopLengthSec;
                _component.perceptualThreshold = this.perceptualThreshold;
                _component.crossFadeAudio = this.crossFadeAudio;
                _component.muteAudio = clip.muteAudio;

                _component.startTime = clip.start;
                _component.endTime = clip.end;
                _component.jumpThreshold = this.jumpThreshold;
                _component.loopArcs = this.loopArcs.ToArray();
                Array.Sort(_component.loopArcs);
                _component.views = this.views;

                _component.enabled = enabled;
                _component.clipIndex = clip.GetIndex();
                _component.timelinePos = clip.timelinePos;
                _component.jumpImmediately = jumpImmediately;
            }
        }

        public void SetUpComponent(GameObject sphere, bool enabled, Clip clip)
        {
            PlayableClip[] playableClips = sphere.GetComponents<PlayableClip>();

            _component = null;
            foreach (PlayableClip playableClip in playableClips)
            {
                if (Mathf.Abs(playableClip.startTime - this.startTime) < 1e-5f && Mathf.Abs(playableClip.endTime - this.endTime) < 1e-5f)
                {
                    GatedClip storyPoint = playableClip as GatedClip;
                    if (storyPoint != null)
                    {
                        GatedClip toDelete = storyPoint;
                        MonoBehaviour.DestroyImmediate(toDelete);
                        break;
                    }
                }
            }
            if (_component == null)
            {
                _component = (GatedClip)sphere.AddComponent(typeof(GatedClip));
            }
            UpdateComponent(enabled, clip);
        }
    }

    [Serializable]
    public class Clip
    {
        public string name;
        public float start;
        public float end;
        public bool insideTimeline;
        public float timelinePos;  // Time position on VideoEditorWindow's timeline.
        public bool muteAudio = false;
        public bool Gated;
        public StoryPoint sp;

        [XmlIgnore]
        public Vector2 scrollPos;
        private int _index;

        [XmlIgnore]
        public PlayableClip _clipComponent = null;
        [XmlIgnore]
        public bool selectingTarget = false;

        public Clip()
        {

        }

        public Clip(string newName, float newStart, float newEnd, bool newInsideTimeline = false, float newTimelinePos = -1f, int newIndex = -1, bool newMuteAudio=false)
        {
            name = newName;
            start = newStart;
            end = newEnd;
            insideTimeline = newInsideTimeline;
            timelinePos = newTimelinePos;

            Gated = false;
            sp = null;

            _index = newIndex;
            muteAudio = newMuteAudio;

        }

        public string GetSelectTargetLabel()
        {
            if (selectingTarget)
            {
                return "OK";
            } else
            {
                return "Select ROI";
            }
        }

        public int GetIndex()
        {
            return _index;
        }

        public bool isTargetView(int f)
        {
            if (sp == null || sp._component == null)
            {
                return false;
            }
            return sp._component.isTargetView(f);
        }

        public float GetGateTimeOnTimeline()
        {
            if (sp == null || sp._component == null)
            {
                return -1;
            }
            return timelinePos + sp.gateTime;
        }

        public void SetCutFile(string fileName)
        {
            if (sp != null)
            {
                sp.ReadCutFile(fileName);
            }
        }
        public void SetAllArcsFile(string fileName)
        {
            if (sp!=null)
            {
                sp.ReadAllArcsFile(fileName);
            }
        }

        public void SetValidArcFile(string fileName)
        {
            if (sp != null)
            {
                sp.ReadValidArcFile(fileName);
            }
        }

        public void SetExtraCostsFile(string fileName)
        {
            if (sp != null)
            {
                sp.ReadExtraCostsFile(fileName);
            }
        }

        public void SetEdgeCostFile(string fileName, GameObject sphere)
        {
            if (sp != null)
            {
                sp.ReadEdgeCostFile(fileName);
            }
        }

        public override string ToString()
        {
            string defaultStr = "Clip Start: " + this.start + ", End: " + this.end;
            if (_clipComponent != null)
            {
                defaultStr += ". ClipComponent: " + _clipComponent.ToString();
            }
            else if (sp != null && sp._component != null)
            {
                defaultStr += ". SP Component: " + sp._component.ToString();
            }
            return defaultStr;
        }

        public override int GetHashCode()
        {
            return string.Format("{0}_{1}_{2}", this.name, this.start, this.end).GetHashCode();
        }

        public override bool Equals(System.Object obj)
        {
            if (obj == null)
            {
                return false;
            }
            Clip otherClip = obj as Clip;
            if (otherClip == null)
            {
                return false;
            }
            return this.name == otherClip.name && Math.Abs(this.start - otherClip.start) < 1e-5f && Math.Abs(this.end - otherClip.end) < 1e-5f;
        }

        public void SetLoopsFolder(string loopFolder)
        {
            if (Gated && sp != null)
            {
                if (sp.LoopsFolder != loopFolder)
                {
                    sp.LoopsFolder = loopFolder;
                    sp.InitializeViews();
                }
            }
        }

        public bool HasViews()
        {
            return sp != null && sp.views != null && sp.views.Length > 1;
        }

        public int GetNumViews()
        {
            if (!HasViews())
            {
                return 0;
            }
            return sp.views.Length;
        }

        public void MarkLoopArcsDirty(int viewNum)
        {
            if (!HasViews())
            {
                return;
            }
            if (viewNum < sp.views.Length)
            {
                for (int l = 0; l < sp.views[viewNum].loopArcs.Count; l++)
                {
                    sp.views[viewNum].loopArcs[l].MarkDirty();
                }
            }
        }

        public List<LoopArc> GetViewLoopArcs(int viewNum)
        {
            if (!HasViews())
            {
                return null;
            }
            if (viewNum < sp.views.Length)
            {
                return sp.views[viewNum].loopArcs;
            }
            return null;
        }

        public void ReadSP(StoryPoint newSP, GameObject sphere)
        {
            sp = newSP;

            if (Gated && !sp.HasComponent())
            {
                sp.SetUpComponent(sphere, this.GetSPEnabled(), this);
            }
        }

        void UpdateClipComponent()
        {
            if (_clipComponent != null)
            {
                _clipComponent.clipName = this.name;
                _clipComponent.startTime = this.start;
                _clipComponent.endTime = this.end;
                _clipComponent.enabled = !this.Gated && this.insideTimeline;
                _clipComponent.clipIndex = this.GetIndex();
                _clipComponent.timelinePos = this.timelinePos;
                _clipComponent.muteAudio = this.muteAudio;
            }
        }

        public void SetUpClipComponent(GameObject sphere)
        {
            PlayableClip[] playableClips = sphere.GetComponents<PlayableClip>();

            _clipComponent = null;
            PlayableClip toDelete = null;
            foreach (PlayableClip playableClip in playableClips)
            {
                if (Mathf.Abs(playableClip.startTime - this.start) < 1e-5f && Mathf.Abs(playableClip.endTime - this.end) < 1e-5f)
                {
                    toDelete = playableClip;
                    MonoBehaviour.DestroyImmediate(toDelete);
                    break;
                }
            }
            _clipComponent = (PlayableClip)sphere.AddComponent(typeof(PlayableClip));
            UpdateClipComponent();
        }

        public bool GetSPEnabled()
        {
            return this.Gated && this.insideTimeline;
        }

        public void RemoveCurrentComponent(GameObject sphere)
        {
            PlayableClip[] playableClips = sphere.GetComponents<PlayableClip>();

            foreach (PlayableClip playableClip in playableClips)
            {
                if (Mathf.Abs(playableClip.startTime - this.start) < 1e-5f && Mathf.Abs(playableClip.endTime - this.end) < 1e-5f)
                {
                    GatedClip storyPoint = playableClip as GatedClip;
                    if (storyPoint != null)
                    {
                        GatedClip toDelete = storyPoint;
                        MonoBehaviour.DestroyImmediate(toDelete);
                        if (sp != null)
                        {
                            sp._component = null;
                        }
                    }
                    else
                    {
                        MonoBehaviour.DestroyImmediate(playableClip);
                        _clipComponent = null;
                    }
                    break;
                }
            }
        }

        public void MarkStoryPoint(GameObject sphere)
        {
            Debug.Log("Mark story point " + Gated + " to " + !Gated);
            RemoveCurrentComponent(sphere);
            Gated = !Gated;
            
            GenerateAndUpdateComponents(sphere);
            HeadTrack headtrack = sphere.GetComponent<HeadTrack>();
            headtrack.RefreshTimeline();
        }

        public void ToggleJumpForward(GameObject sphere)
        {
            if (sp != null)
            {
                sp.jumpImmediately = !sp.jumpImmediately;
                Debug.Log("Marked jump immediately: " + sp.jumpImmediately);
                sp.UpdateComponent(GetSPEnabled(), this);
            }
        }

        public void ToggleCrossFadeAudio(GameObject sphere)
        {
            if (sp != null)
            {
                this.sp.crossFadeAudio = !this.sp.crossFadeAudio;
                sp.UpdateComponent(GetSPEnabled(), this);
                Debug.Log("Marked cross fade audio: " + sp._component.crossFadeAudio);
            }
        }

        public void ToggleMuteAudio(GameObject sphere)
        {
            this.muteAudio = !this.muteAudio;
            if (sp != null && sp.HasComponent())
            {
                sp.UpdateComponent(this.GetSPEnabled(), this);
            }

            if (_clipComponent != null)
            {
                UpdateClipComponent();
            }
        }

        public void SetTimelinePos(float newPos)
        {
            this.timelinePos = newPos;
            UpdateClipComponent();
            if (sp != null && sp.HasComponent())
            {
                sp.UpdateComponent(this.GetSPEnabled(), this);
            }
        }

        public void GenerateAndUpdateComponents(GameObject sphere)
        {
            if (Gated && sp == null)
            {
                sp = new StoryPoint(0, 0, 1, 1, -1f, true, this.start, this.end, false, 1, 1, StoryPoint.DEFAULT_PERCEPTUAL_THRES, "", sphere);
            }

            if (Gated && !sp.HasComponent())
            {
                sp.SetUpComponent(sphere, this.GetSPEnabled(), this);
            }

            if (sp != null)
            {
                sp.UpdateComponent(this.GetSPEnabled(), this);
            }

            if (!Gated && _clipComponent == null)
            {
                this.SetUpClipComponent(sphere);
            }
            if (_clipComponent != null)
                UpdateClipComponent();
        }

        public float Duration()
        {
            return end - start;
        }

        public float TimelineEndPos()
        {
            return timelinePos + Duration();
        }

        public void SetInsideTimeline(bool newVal, float newPos, GameObject sphere)
        {
            insideTimeline = newVal;
            timelinePos = newPos;
            GenerateAndUpdateComponents(sphere);
        }

        public string GetWindowName()
        {
            return "(" + start + " - " + end + ") Settings";
        }

        public string GetClipPreviewDes()
        {
            return "Clip: (" + start + " - " + end + ")";
        }
    }
}