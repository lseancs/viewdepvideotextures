#include <stdio.h>
#include "graph.h"
#include"cnpy.h"
#include <opencv2/opencv.hpp>
#include <boost/program_options.hpp>
#include <iostream>
#include <boost/filesystem.hpp>
#include <boost/range/iterator_range.hpp>
#include <vector>
#include <assert.h>
#include <float.h>
#include <limits>
#include <nlohmann/json.hpp>
#define INFINITE_D (numeric_limits<float>::max())

using namespace std;
using namespace cnpy;
using namespace cv;
using namespace boost::program_options;
using namespace boost::filesystem;
using namespace nlohmann;


typedef Graph<float,float,float> GraphType;

void writeAllArcsJson(vector<vector<int>> allArcs, variables_map vm) {
  string outputDir = vm["outputDir"].as<string>();
  path outputPath = outputDir / "allArcs.json";
  string outputFile = outputPath.string();
  
  std::ofstream o(outputFile);
  json finalJsonArray = json::array();
  
  for (int i = 0; i < allArcs.size(); i++) {
    json currentArray = json::array();
    for (int j = 0; j < allArcs[i].size(); j++) {
      currentArray.push_back(allArcs[i][j]);
    }
    finalJsonArray.push_back(currentArray);
  }
  
  o << std::setw(4) << finalJsonArray << endl;
}

void InvertTarget(int* x1, int* x2, int numViewingDirection) {
  cout << "INVERSE GATE! BEFORE: " << *x1 << " to " << *x2 << endl;
  int originalX1 = *x1;
  *x1 = *x2 + 1;
  *x2 = originalX1 - 1;
  
  if (*x1 > numViewingDirection - 1) {
    *x1 = *x1 - numViewingDirection;
  }
  if (*x2 < 0) {
    *x2 = *x2 + numViewingDirection;
  }
  cout << "INVERSE GATE! After: " << *x1 << " and " << *x2 << endl;
}

Mat SetupGraph(GraphType* g, const vector<vector<int>>& bestArcs, const vector<vector<int>>& allArcs, const vector<Mat>& costMatrices, int gateFrame, variables_map vm)
{
  assert(bestArcs.size() == costMatrices.size()); // Number of viewing directions.
  assert(bestArcs[0].size() == costMatrices[0].rows); // Number of (total) frames in each viewing direction.
  int numRawFrames = gateFrame + 2; // Only creating nodes for frames up to gate frame + 1 (so cutting on gate frame is also possible).

  int numFrames = numRawFrames + numRawFrames - 1;  // Number of nodes per viewing direction, including buffer nodes.
  int numViewingDirection = bestArcs.size();
  int numNodes = numFrames * bestArcs.size();  // Total number of nodes in the graph
  int x1 = vm["ROIstart"].as<int>();
  int x2 = vm["ROIend"].as<int>();
  
  if (vm["offscreen"].as<bool>()) {
    InvertTarget(&x1, &x2, numViewingDirection);
  }
  
  int x3 = 10000000;
  int x4 = x3;
  if (x1 > x2) {
    x3 = 0;
    x4 = x2;
    x2 = numViewingDirection - 1;
  }

  g -> add_node(numNodes);
  
  Mat edgeCosts(numViewingDirection, numRawFrames - 1, CV_32FC1);
  
  // First add all the infinite weights from s and to t.
  int count = 0;
  for (int s = 0; s < numNodes; s += numFrames) {
    count ++;
    g -> add_tweights( s,   /* capacities */  INFINITE_D, 0 );
  }
  assert(count == numViewingDirection);
  
  count = 0;
  for (int t = numFrames - 1; t < numNodes; t += numFrames) {
    count++;
    g -> add_tweights( t,   /* capacities */  0, INFINITE_D);
  }
  
  assert(count == numViewingDirection);
  
  // Add edges between adjacent frames. Cost determined by bestArcs.
  for (int row = 0; row < numViewingDirection; row++) {
    for (int f = 0; f < numRawFrames - 1; f++) {
      float edgeCost;
      int bestArc = bestArcs[row][f];
      if ((row >= x1 && row <= x2) || (row >= x3 && row <= x4)) {  // If view satisfies gate condition
        if (f == numRawFrames - 2) {
          edgeCost = 0;
        }
        else {
          edgeCost = INFINITE_D;
        }
      }
      else if (bestArc < 0) {
        int nextBestArc = allArcs[row][f];
        float extraCost = nextBestArc >= 0 ? costMatrices[row].at<float>(f, nextBestArc) : 10000000;
        edgeCost = extraCost;
      }
      else {
        edgeCost = 0;
      }
      edgeCosts.at<float>(row, f) = edgeCost;
      
      g -> add_edge(row * numFrames + 2*f + 1, row * numFrames + 2*f + 2,  INFINITE_D, 0);
    }
  }
  
  // Add infinite edges between nodes in adjacent viewing directions.
  for (int f = 0; f < numRawFrames - 1; f++) {
    for (int row = 0; row < numViewingDirection; row++) {
      int bottomRow = (row+1) < numViewingDirection ? (row+1) : (row+1-numViewingDirection);
      int nodeCurrent = row * numFrames + 2 * f + 1;
      int nodeBottom = bottomRow * numFrames + 2 * f + 2;
      g -> add_edge( nodeCurrent, nodeBottom, INFINITE_D, 0);
      
      int topRow = (row-1) >= 0 ? (row-1) : (row-1+numViewingDirection);
      int nodeTop = topRow * numFrames + 2 * f + 2;
      nodeCurrent = row * numFrames + 2 * f + 1;
      g -> add_edge( nodeCurrent, nodeTop, INFINITE_D, 0);
      
      int bottomBottomRow = (row+2) < numViewingDirection ? (row+2) : (row+2-numViewingDirection);
      int nodeBottomBottom = bottomBottomRow * numFrames + 2 * f + 2;
      nodeCurrent = row * numFrames + 2 * f + 1;
      g -> add_edge( nodeCurrent, nodeBottomBottom, INFINITE_D, 0);
      
      int topTopRow = (row-2) >= 0 ? (row-2) : (row-2+numViewingDirection);
      int nodeTopTop = topTopRow * numFrames + 2 * f + 2;
      nodeCurrent = row * numFrames + 2 * f + 1;
      g -> add_edge( nodeCurrent, nodeTopTop, INFINITE_D, 0);
    }
  }
  return edgeCosts;
}

void AssignEdgeCosts(GraphType* g, const vector<vector<int>>& bestArcs, int gateFrame, const Mat& edgeCosts) {
  int numRawFrames = gateFrame + 2; // Only creating nodes for frames up to gate frame + 1 (so cutting on gate frame is also possible).
  int numFrames = numRawFrames + numRawFrames - 1;  // Number of nodes per viewing direction, including buffer nodes.
  int numViewingDirection = bestArcs.size();
  for (int row = 0; row < numViewingDirection; row++) {
    for (int f = 0; f < numRawFrames - 1; f++) {
      g -> add_edge(row * numFrames + 2*f, row * numFrames + 2*f + 1,  edgeCosts.at<float>(row, f), 0);
    }
  }
}

bool containedBy(tuple<int, int> block1, tuple<int, int> block2) {  // Is block1 contained by block2
  // Small penalty for blocks with no vertical overlap.
  return get<0>(block2) <= get<0>(block1) && get<1>(block1) <= get<1>(block2);
}

void UpdateEdgeCosts(Mat* edgeCosts, variables_map vm) {
  int numViewingDirection = edgeCosts->rows;
  assert(numViewingDirection == 40);
  int x1 = vm["ROIstart"].as<int>();
  int x2 = vm["ROIend"].as<int>();
  if (vm["offscreen"].as<bool>()) {
    InvertTarget(&x1, &x2, numViewingDirection);
  }
  
  int x3 = 10000000;
  int x4 = x3;
  if (x1 > x2) {
    x3 = 0;
    x4 = x2;
    x2 = numViewingDirection - 1;
  }
  
  int region = vm["loopDuration"].as<int>();
  
  vector<vector<tuple<int, int>>> AllBlocks;
  
  for (int i = 0; i < edgeCosts->rows; i++) {
    
    vector<tuple<int, int>> blocks;
    
    if ((i >= x1 && i <= x2) || (i >= x3 && i <= x4)) {
      AllBlocks.push_back(blocks);
      continue;
    }
    
    int start = -1;
    int end = -1;
    bool insideBlock = false;
    for (int f = 0; f < edgeCosts->cols; f++) {
      if (edgeCosts->at<float>(i, f) == 0) {
        if (!insideBlock) {
          insideBlock = true;
          start = f;
        }
      }
      else {
        if (insideBlock) {
          insideBlock = false;
          end = f - 1;
          blocks.push_back(make_tuple(start, end));
        }
      }
      if (f == edgeCosts->cols - 1 && insideBlock) {
        insideBlock = false;
        end = f;
        blocks.push_back(make_tuple(start, end));
      }
    }
    AllBlocks.push_back(blocks);
    
    for (auto& block : blocks) {
      int blockLength = get<1>(block) - get<0>(block) + 1;
      int halfLength = blockLength / 2;
      
      float slope = 1.0f / (float)(region);
      for (int s = 0; s < blockLength; s++) {
        float newCost;
        
        if (get<1>(block) != vm["gateFrame"].as<int>() && blockLength < region*2) {
          slope = 1.0f / (float)(halfLength+1);
          if (s >= halfLength) {
            newCost = 1 + slope * (s - halfLength);
          }
          else {
            newCost = 1 + 1 - slope * s;
          }
        }
        else if (s >= blockLength - 1 - region) {
          if (get<1>(block) == vm["gateFrame"].as<int>()) {
            newCost = 0;
          }
          else {
            newCost = slope * (s - (blockLength-1 - region));
          }
        }
        else if (s < region) {
          newCost = 1 - slope * s;
        }
        else {
          newCost = 0.f;
        }
        edgeCosts->at<float>(i, get<0>(block) + s) = newCost;
      }
    }
    
  }
  
  for (int r = 0; r < AllBlocks.size(); r++) {
    int topRow = r - 1;
    int bottomRow = r+1;
    
    
    if (topRow < 0) {
      topRow += numViewingDirection;
    }
    if (bottomRow >= numViewingDirection) {
      bottomRow -= numViewingDirection;
    }
    
    if ((r >= x1 && r <= x2) || (r >= x3 && r <= x4)) {
      continue;
    }
    
    for (auto & blockC : AllBlocks[r]) {
      bool overlapsTop = false;
      bool overlapsBottom = false;
      
      if ((topRow >= x1 && topRow <= x2) || (topRow >= x3 && topRow <= x4)) {
        overlapsTop = false;
      }
      else {
        for (auto& blockT : AllBlocks[topRow]) {
          if (containedBy(blockC, blockT)) {
            overlapsTop = true;
            break;
          }
        }
      }
      
      if ((bottomRow >= x1 && bottomRow <= x2) || (bottomRow >= x3 && bottomRow <= x4)) {
        overlapsBottom = false;
      }
      else {
        for (auto& blockB : AllBlocks[bottomRow]) {
          if (containedBy(blockC, blockB)) {
            overlapsBottom = true;
            break;
          }
        }
      }
      
      float verticalPenalty = 0.0f;
      if (!overlapsTop) {
        verticalPenalty += 0.1f;
      }
      if (!overlapsBottom) {
        verticalPenalty += 0.1f;
      }
      
      // Apply penalty to block.
      if (verticalPenalty > 0.0f) {
        for (int s = get<0>(blockC); s <= get<1>(blockC); s++) {
          edgeCosts->at<float>(r, s) = edgeCosts->at<float>(r, s)  + verticalPenalty;
        }
      }
    }
  }
}

void printOutCostMatrix(NpyArray arr) {
  int height = (int)arr.shape[0];
  int width = (int)arr.shape[1];
  
  float* floatArray = arr.data<float>();
  
  int count = 0;
  for (int i = 0; i < height; i++) {
    for (int j = 0; j < width; j++) {
      printf("%f ", floatArray[count]);
      count++;
    }
    printf("\n");
  }
}

Mat convertDataToMat(NpyArray arr, string saveToFile="") {
  float* floatArray = arr.data<float>();
  int height = (int)arr.shape[0];
  int width = (int)arr.shape[1];
  Mat M(height, width, CV_32FC1);
  
  memcpy(M.data, floatArray, height*width*sizeof(float));
  
  if (saveToFile != "") {
    cout << "Writing cost matrices to " << saveToFile << endl;
    FileStorage file(saveToFile, FileStorage::WRITE);
    file << "raw_costs" << M;
    file.release();
  }
  
  return M;
}

NpyArray ReadCostMatrix(string filename) {
  NpyArray arr = npy_load(filename);
  return arr;
}

void checkForSymmetry(const Mat& m) {
  Mat t;
  transpose(m, t);
  Mat diff = m != t;
  assert(countNonZero(diff) == 0);
}

void convolveGaussianKernel(Mat* m, int loopDuration) {  // cross fade time in number of frames.
  checkForSymmetry(*m);
  Mat kernel = Mat::zeros( Size(loopDuration*2-1, loopDuration*2-1), CV_32F );
  for(int i = loopDuration - 1; i < loopDuration*2-1; i ++) {
    kernel.at<float>(i, i) = 1;
  }
  filter2D(*m, *m, -1 , kernel, Point(-1,-1), 0, BORDER_ISOLATED);
}

// For one viewing direction. allArcs: Arcs with minimum perceptual cost, given that min loop length is met. May be above perceptual threshold.
vector<int> FindValidArcs(Mat* m, float perceptualThreshold, int minLength, vector<int>* allArcs) {
  vector<int> arcs;
  assert(m->rows == m->cols);
  
  for (int r = 0; r < m->rows; r++) {
    int arc = -1;
    float minCost = INFINITE_D;
    float minCostUnderThres = INFINITE_D;
    int minArcTo = -1;
    for (int c = 0; c <= r - minLength; c++) {
      if (m->at<float>(r, c) < minCost) {
        minCost =m->at<float>(r, c);
        minArcTo = c;  // Arc with minimum perceptual cost, given that min loop length is met.
      }
      if (m->at<float>(r, c) <= perceptualThreshold && m->at<float>(r, c) <= minCostUnderThres) {
        arc = c;  // Arc with minimum perceptual cost, given that min loop length AND perceptual threshold are met.
        minCostUnderThres = m->at<float>(r, c);
      }
    }

    assert(minArcTo == -1 || minArcTo <= r - minLength);
    allArcs->push_back(minArcTo);
    assert(arc == -1 || arc <= r - minLength);
    arcs.push_back(arc);
  }
  
  return arcs;  // Arcs with minimum perceptual cost, given that min loop length AND perceptual threshold are met.
}

Mat ConstructGraph(GraphType* g, variables_map vm, vector<string> filePaths, vector<vector<int>>* bestArcs, vector<vector<int>>* allArcs, vector<Mat>* costMatrices, float perceptualThreshold, bool writeCosts) {
  
  for (int p = 0; p < filePaths.size(); p++) {
    Mat mat = convertDataToMat(ReadCostMatrix(filePaths.at(p)), "");
    mat.setTo(0, mat < 0);
    
    costMatrices->push_back(mat);
  }
  
  // Convolve gaussian kernel of size loopDuration.
  int loopDuration = vm["loopDuration"].as<int>();
  if (loopDuration > 1) {
    cout << "Loop duration is " << loopDuration << " frames." << endl;
    for (int m = 0; m < costMatrices->size(); m++) {
      convolveGaussianKernel(&(costMatrices->at(m)), loopDuration);  // Equation (1) in Appendix.
      costMatrices->at(m).setTo(0, costMatrices->at(m) < 0);
    }
  }
  
  if (writeCosts) {
    for (int p = 0; p < costMatrices->size(); p++) {
      path outputDir = path(vm["outputDir"].as<string>());
      path fn = path( to_string(p) + "_cost_matrices.xml");
      string finalStr = (outputDir / fn).string();

      cout << "Writing matrix to " << finalStr << endl;
      FileStorage file(finalStr, FileStorage::WRITE);
      file << "filtered_costs" << (*costMatrices)[p];
      file.release();
    }
  }
  
  // Find best arcs for each frame based on perceptual threshold, minLength
  for (int m = 0; m < costMatrices->size(); m++) {
    vector<int> allArcsInView;
    vector<int> arcs = FindValidArcs(&(costMatrices->at(m)), perceptualThreshold, vm["minLength"].as<int>(), &allArcsInView);
    assert(arcs.size() == costMatrices->at(m).rows);
    assert(allArcsInView.size() == costMatrices->at(m).rows);
    bestArcs->push_back(arcs);  // Best backward arc satisfying all user thresholds (perceptual threshold AND minlength). If none exists, then -1.
    allArcs->push_back(allArcsInView);  // Backward arc with lowest perceptual cost that satisfies minLength. May not satisfy user-set perceptual threshold.
  }
  
  // Construct nodes up to gateFrame only.
  int gateFrame = vm["gateFrame"].as<int>();
  cout << "Gate frame is " << gateFrame << endl;
  Mat edgeCosts;
 
  edgeCosts = SetupGraph(g, *bestArcs, *allArcs, *costMatrices, gateFrame, vm);  // Buffer edge costs for entire graph.
  UpdateEdgeCosts(&edgeCosts, vm);  // Update with heuristic weights.
  AssignEdgeCosts(g, *bestArcs, gateFrame, edgeCosts);  // Apply new edge costs to graph.
  
  return edgeCosts;
}

void writeJson(vector<vector<float>> arr, variables_map vm, string name) {
  string outputDir = vm["outputDir"].as<string>();
  path outputPath = outputDir / name;
  string outputFile = outputPath.string();
  std::ofstream o(outputFile);
  json finalJsonArray = json::array();
  
  for (int i = 0; i < arr.size(); i++) {
    json currentArray = json::array();
    for (int j = 0; j < arr[i].size(); j++) {
      currentArray.push_back(arr[i][j]);
    }
    finalJsonArray.push_back(currentArray);
  }
  
  o << std::setw(4) << finalJsonArray << endl;
}

void writeJson(vector<vector<int>> arr, variables_map vm, string name) {
  string outputDir = vm["outputDir"].as<string>();
  path outputPath = outputDir / name;
  string outputFile = outputPath.string();
  std::ofstream o(outputFile);
  json finalJsonArray = json::array();
  
  for (int i = 0; i < arr.size(); i++) {
    json currentArray = json::array();
    for (int j = 0; j < arr[i].size(); j++) {
      currentArray.push_back(arr[i][j]);
    }
    finalJsonArray.push_back(currentArray);
  }
  
  o << std::setw(4) << finalJsonArray << endl;
}

void writeEdgeCosts(const Mat& edgeCosts, string outputDir) {
  std::ostringstream stringStream;
  stringStream << outputDir << "/edge_cost_matrix.xml";
  FileStorage file(stringStream.str(), FileStorage::WRITE);
  
  file << "edge_costs" << edgeCosts;
  file.release();
}

int FindMinValidArc(const Mat& m, int row, int firstFrameCut) {
  float minCost = INFINITE_D;
  int minArcTo = -1;
  for (int c = 0; c <= firstFrameCut; c++) {
    if (m.at<float>(row, c) < minCost) {
      minCost = m.at<float>(row, c);
      minArcTo = c;
    }
  }
  return minArcTo;
}

bool GetValidArcsFromCut(const vector<vector<int>>& cut, const vector<vector<int>>& allArcs, const vector<Mat>& costMatrices, vector<vector<int>>* validArcs, vector<vector<float>>* extraCosts, float threshold) {
  
  bool changed = false;
  float totalCost = 0;
  for (int i = 0; i < cut.size(); i++) {
    vector<int> validArcsInView;
    vector<float> extraCostsInView;
    
    if (cut[i].size() == 0) {  // Happens when view satisfies gate condition.
      validArcs->push_back(validArcsInView);
      extraCosts->push_back(extraCostsInView);
      continue;
    }
    int firstFrameCut = cut[i][0];
    validArcsInView.push_back(allArcs[i][firstFrameCut]);
    extraCostsInView.push_back(0);
    for (int j = 1; j < cut[i].size(); j++) {
      int cutFrame = cut[i][j];
      int toFrame = allArcs[i][cutFrame];
      if (toFrame < firstFrameCut) {  // If cut[i] contains toFrame OR toFrame is before firstFrameCut.
        validArcsInView.push_back(toFrame);
        extraCostsInView.push_back(0);
      }
      else {
        changed=true;
        int minArc = FindMinValidArc(costMatrices[i], cutFrame, firstFrameCut);
        float newCost = (costMatrices[i].at<float>(cutFrame, minArc) <= threshold) ? 0 : costMatrices[i].at<float>(cutFrame, minArc);
        totalCost += newCost;
        extraCostsInView.push_back(newCost);
        validArcsInView.push_back(minArc);
      }
    }
    validArcs->push_back(validArcsInView);
    extraCosts->push_back(extraCostsInView);
  }
  cout << "changed? " << changed << ". Total extra cost: " << totalCost << endl;
  return changed;
}

vector<vector<int>> findCut(GraphType* g, const Mat& edgeCosts, const vector<vector<int>>& bestArcs, const vector<vector<int>>& allArcs, const vector<Mat>& costMatrices, float* totalCost) {
  
  assert(bestArcs.size() == costMatrices.size()); // Number of viewing directions.
  assert(bestArcs[0].size() == costMatrices[0].rows); // Number of (total) frames in each viewing direction.
  
  int numRawFrames = edgeCosts.cols + 1;
  int numFrames = numRawFrames + numRawFrames - 1;  // Number of nodes per viewing direction, including buffer nodes.
  int numViewingDirection = bestArcs.size();
  int numNodes = numFrames * bestArcs.size();  // Total number of nodes in the graph
  
  float cutCost = 0;
  vector<vector<int>> cut;
  
  for (int r = 0; r < edgeCosts.rows; r++) {
    vector<int> cutsInViewingDirection;
    for (int f = 0; f < edgeCosts.cols; f++) {
      int nodeLeft = r * numFrames + 2*f;
      int nodeRight = r * numFrames + 2*f + 1;
      if (g->what_segment(nodeLeft) != g->what_segment(nodeRight)) {
        cutCost += edgeCosts.at<float>(r, f);  // Edge cost is different from raw cost. Edge cost is 0 if backward arc satisfies user-set thresholds.
        cutsInViewingDirection.push_back(f);
      }
    }
    cut.push_back(cutsInViewingDirection);
  }
  cout << "Total cut cost: " << cutCost << endl;
  *totalCost = cutCost;
  return cut;
}

float getXFromFileName(string s) {
  stringstream test(s);
  string segment;
  vector<string> seglist;
  
  while(getline(test, segment, '_'))
  {
    seglist.push_back(segment);
  }
  float x = stof(seglist.at(seglist.size() - 2));
  return x;
}

bool comparingNumpyFiles(string a, string b) {
  float x1 = getXFromFileName(a);
  float x2 = getXFromFileName(b);
  return x1 < x2;
}

vector<string> GetNumpyFiles(string directory) {
  vector<string> numpyFiles;
  for(auto& entry : boost::make_iterator_range(directory_iterator(directory), {})) {
    if (entry.path().extension().string() == ".npy") {
      numpyFiles.push_back(entry.path().string());
    }
  }
  
  sort(numpyFiles.begin(), numpyFiles.end(), comparingNumpyFiles);
  return numpyFiles;
}

float findCutCost(vector<string> numpyFiles, variables_map vm, float perceptualThreshold) {
  GraphType *g = new GraphType(numpyFiles.size() * 30 * 10, numpyFiles.size() * 30 * 10);
  vector<vector<int>> bestArcs;
  vector<vector<int>> allArcs;
  vector<Mat> costMatrices;
  Mat edgeCosts = ConstructGraph(g, vm, numpyFiles, &bestArcs, &allArcs, &costMatrices, perceptualThreshold, false);  // Updated buffer edge costs (after applying heuristics).
  float flow = g -> maxflow();
  
  vector<vector<int>> cut;
  float totalCost;
  cut = findCut(g, edgeCosts, bestArcs, allArcs, costMatrices, &totalCost);
  delete g;
  
  return totalCost;
}

// Find lowest threshold that still gives us a cut whose total cost is under that threshold. Left means not good enough cut. Right is ok.
float findThreshold(vector<string> numpyFiles, variables_map vm, int left=0, int right=15000) {
  
  cout << "Left is " << left << ". Right is " << right << endl;
  if (right - left <= 1) {
    return right;
  }
  int mid = (int)(left + right) / 2.0f;
  float totalCost = findCutCost(numpyFiles, vm, mid);
  cout << "mid is " << mid << ". Total cost is " << totalCost << endl;
  if (totalCost < mid) {
    right = mid;
    cout << "Assigning "<< right << " to right." << endl;
  }
  else {
    left = mid;
    cout << "Assigning "<< left << " to left." << endl;
  }
  return findThreshold(numpyFiles, vm, left, right);
}

int main(int argc, char **argv)
{
  
  // Command line argument parsing.
  options_description desc("Allowed options");
  desc.add_options()
  ("help", "Print help message.")
  ("loopDuration", value<int>()->default_value(15), "Length of loop in number of frames.")
  ("minLength", value<int>()->default_value(30), "Minimum length of loop in number of frames.")
  ("perceptualThreshold", value<float>()->default_value(2000), "User-set perceptual threshold. Could also automatically find the lowest threshold such that the total cut cost is under that threshold; see findThreshold parameter.")
  ("gateFrame,G", value<int>(), "Gate frame number.")
  ("inputDir,I", value<string>(), "Input directory of .npy files of cost matrices.")
  ("ROIstart", value<int>()->default_value(4), "View in which ROI starts (inclusive).")
  ("ROIend", value<int>()->default_value(13), "View in which ROI ends (inclusive).")
  ("outputDir,O", value<string>(), "Output directory for cut results, cost matrices, etc.")
  ("writeCosts", value<bool>()->default_value(true), "Whether or not to write costs to xml files.")
  ("findThreshold", value<bool>()->default_value(false), "Whether or not to automatically find minimum perceptual threshold such that the total cut cost is under that threshold. Uses binary search to find minimum threshold.")
  ("offscreen", value<bool>()->default_value(false), "Whether or not to use offscreen gate (i.e., NOT look at ROI to proceed).")
  ;
  
  variables_map vm;
  store(parse_command_line(argc, argv, desc), vm);
  notify(vm);
  
  if (vm.count("help")) {
    cout << desc << "\n";
    return 1;
  }
  
  if (vm.count("gateFrame") == 0) {
    cout << "Need to specify gate frame. Exiting." << "\n";
    return 1;
  }
  
  if (vm.count("inputDir") == 0) {
    cout << "Need to specify input directory. Exiting." << "\n";
    return 1;
  }
  
  if (vm.count("outputDir") == 0) {
    cout << "Need to specify output directory. Exiting." << "\n";
    return 1;
  }
  
  for (const auto& it : vm) {
    std::cout << it.first.c_str() << " = ";
    auto& value = it.second.value();
    if (auto v = boost::any_cast<int>(&value))
      cout << *v;
    else if (auto v = boost::any_cast<string>(&value))
      cout << *v;
    else if (auto v = boost::any_cast<float>(&value))
      cout << *v;
    else
      cout << "error";
    cout << endl;
  }
  
  vector<string> numpyFiles;
  if(is_directory(vm["inputDir"].as<string>())) {
    std::cout << vm["inputDir"].as<string>() << " is a directory containing:\n";
    numpyFiles = GetNumpyFiles(vm["inputDir"].as<string>());
  }
  
  if (!vm["findThreshold"].as<bool>()) {  // Don't automatically find best threshold.
    GraphType *g = new GraphType(numpyFiles.size() * 30 * 10, numpyFiles.size() * 30 * 10);
    vector<vector<int>> bestArcs;  // "Best" backward arc from each frame, i.e. satisfies BOTH perceptual and min loop length thresholds.
    vector<vector<int>> allArcs;  // Lowest perceptual cost arc from each frame that satisfies min loop length threshold. Note that cost may not satisfy user-set perceptual threshold.
    vector<Mat> costMatrices;
    Mat edgeCosts = ConstructGraph(g, vm, numpyFiles, &bestArcs, &allArcs, &costMatrices, vm["perceptualThreshold"].as<float>(), vm["writeCosts"].as<bool>());
    float flow = g -> maxflow();
    
    vector<vector<int>> cut;
    float totalCost;
    cut = findCut(g, edgeCosts, bestArcs, allArcs, costMatrices, &totalCost);
      
    vector<vector<int>> validArcs;
    vector<vector<float>> extraCosts;
    bool changed = GetValidArcsFromCut(cut, allArcs, costMatrices, &validArcs, &extraCosts, vm["perceptualThreshold"].as<float>());
    
    writeEdgeCosts(edgeCosts, vm["outputDir"].as<string>());
    writeJson(cut, vm, "cut.json");
    writeJson(validArcs, vm, "valid.json");
    writeJson(extraCosts, vm, "extraCosts.json");
    writeAllArcsJson(allArcs, vm);
  }
  else {
    cout << "Finding best threshold!" << endl;
    float threshold = findThreshold(numpyFiles, vm, 0, 100000);
    
    GraphType *g = new GraphType(numpyFiles.size() * 30 * 10, numpyFiles.size() * 30 * 10);
    vector<vector<int>> bestArcs;  // "Best" backward arc from each frame, i.e. satisfies BOTH perceptual and min loop length thresholds.
    vector<vector<int>> allArcs;  // Lowest perceptual cost arc from each frame that satisfies min loop length threshold. Note that cost may not satisfy user-set perceptual threshold.
    vector<Mat> costMatrices;
    Mat edgeCosts = ConstructGraph(g, vm, numpyFiles, &bestArcs, &allArcs, &costMatrices, threshold,  vm["writeCosts"].as<bool>());
    float flow = g -> maxflow();
    
    vector<vector<int>> cut;
    float totalCost;
    cut = findCut(g, edgeCosts, bestArcs, allArcs, costMatrices, &totalCost);
    cout <<"Threshold is " << threshold << endl;
    cout << "Flow: " << flow << ". Total cost: " << totalCost << endl;
    
    vector<vector<int>> validArcs;
    vector<vector<float>> extraCosts;
    bool changed = GetValidArcsFromCut(cut, allArcs, costMatrices, &validArcs, &extraCosts, threshold);
    
    writeEdgeCosts(edgeCosts, vm["outputDir"].as<string>());
    writeJson(cut, vm, "cut.json");
    writeJson(validArcs, vm, "valid.json");
    writeJson(extraCosts, vm, "extraCosts.json");
    writeAllArcsJson(allArcs, vm);
  }

	return 0;
}
