import xml.etree.ElementTree as ET
import numpy as np
import matplotlib.pyplot as plt
import math
from moviepy.video.io.VideoFileClip import VideoFileClip
import scipy.misc

import subprocess
import os
import sys
import argparse
import cv2
from moviepy.editor import *
import scipy.misc

#https://www.mathworks.com/help/map/ref/areaquad.html
def areaQuad(lon1, lat1, lon2, lat2): # all in degrees
#     A = 2*pi*R^2 |sin(lat1)-sin(lat2)| |lon1-lon2|/360
    return (math.pi/180.)*np.abs(np.sin(np.radians(lat1))-np.sin(np.radians(lat2))) * np.abs(lon1-lon2)

def convert_pixel_to_lon_lat(xx, yy, width, height):
    xx = xx - width/2
    yy = height / 2 - yy
    lon_per_pixel = 360. / width
    lat_per_pixel = 180. / height
    pixel_left = xx
    pixel_right = xx+1
    pixel_top = yy
    pixel_bottom = yy - 1
    lon1, lon2 = lon_per_pixel * pixel_left, lon_per_pixel * pixel_right
    lat1, lat2 = lat_per_pixel * pixel_bottom, lat_per_pixel * pixel_top
    return lon1, lat1, lon2, lat2

def computeScalingMapOnEquirect(width, height, normalized=True):
    x = np.arange(0, width)
    y = np.arange(0, height)
    xx, yy = np.meshgrid(x, y)
    lon1, lat1, lon2, lat2 = convert_pixel_to_lon_lat(xx, yy, width, height)
    scaled_factors = areaQuad(lon1, lat1, lon2, lat2)
    if not normalized:
        return scaled_factors
    else:
        normalized_scaled_factors = scaled_factors / np.amin(scaled_factors)
        return normalized_scaled_factors
    
def getEdgeDir(equirect_video_path):
    preprocess_dir = getPreprocessDir(equirect_video_path)
    edgeDir = os.path.join(preprocess_dir, "edges")
    if not os.path.exists(edgeDir):
        os.makedirs(edgeDir)
    return edgeDir

def getPreprocessDir(vid):
    basename = os.path.basename(vid).split(".mp4")[0]
    preprocess_dir = os.path.join(os.path.dirname(vid), "{}-preprocess".format(basename))
    if not os.path.isdir(preprocess_dir):
        os.makedirs(preprocess_dir)
    return preprocess_dir
    
def compute_edge_masks(equirect_video_path, size, fps, lowThreshold, highThreshold):
    frames = getFrames(equirect_video_path, resized=size, gray=True)
    framesColor = getFrames(equirect_video_path, resized=size, gray=False)
    preprocess_dir = getPreprocessDir(equirect_video_path)
    edge_fn = os.path.join(preprocess_dir, "edges_@{}.mp4".format(size[0]))
    
    edges_gray = []
    edges_clr = []
    for i in range(len(frames)):
        edges = cv2.Canny(frames[i], lowThreshold, highThreshold, apertureSize=3, L2gradient=True)
        assert np.all((edges == 0) | (edges == 255))
        edges_gray.append(edges)
        edges_color = cv2.bitwise_and(framesColor[i], framesColor[i], mask=edges)
        
        edges_clr.append(edges_color)
        
    edgeDir = getEdgeDir(equirect_video_path)
    print(edgeDir)
    for i in range(len(edges_gray)):
        fn = os.path.join(edgeDir, "{}.png".format(i))
        scipy.misc.imsave(fn, edges_gray[i])
        
    new_clip = ImageSequenceClip(edges_clr, fps=29.97, with_mask=False)
    new_clip.write_videofile(edge_fn) 
    
    original = getEdges(equirect_video_path)
    assert len(edges_gray) == len(original), "Edges vid has length: {}. original has length: {}".format(len(edges_gray), len(original))
    for i in range(len(original)):
        assert np.all(edges_gray[i] == original[i]) and edges_gray[i].dtype == original[i].dtype

    
def getEdges(equirect_video_path):
    edge_dir = getEdgeDir(equirect_video_path)
    raw_fn = os.listdir(edge_dir)
    filtered_fn = list(filter(lambda x: not x.startswith("."), raw_fn))
    edges_fn = sorted(filtered_fn, key=lambda x: int(x.split(".png")[0]))
    assert len(edges_fn) > 0, "Need to run compute edge masks first"
    edges = []
    for fn in edges_fn:
        image = scipy.misc.imread(os.path.join(edge_dir, fn))
        edges.append(image)
    edges = np.stack(edges, axis=0)
    return edges

# Vertical fov: 96.01604 degrees (Oculus Rift headset vertical FOV)
# Horizontal fov: 180 degrees
def generateCostMatrices(vid, y_value, size, hfov=80.65347, vfov=180):  # fovs are in degrees. Oculus headset FOVs.
    center_y = y_value
    x_step = int(size[0] / 40)
    center_xs = np.arange(0, size[0], x_step)
    width_half = math.ceil(hfov / 2 / 360 * size[0])
    height_half = math.ceil(vfov / 2 / 180 * size[1])
    print("Center xs: {}".format(center_xs))
    print("FOV of {}, {} with size {}, {} resolution: width half: {}. Height half: {}".format(
        hfov, vfov, size[0], size[1], width_half, height_half))
    numFrames = getNumFrames(vid)
    
    for x in center_xs:
        topLeft, botRight = getSATBounds(x, center_y, width_half, height_half, size[0], size[1])
        print("Width/Height of {}, {} with center {}, {} has SAT bounds: {}, {}".format(width_half, height_half, x, center_y, topLeft, botRight))
        cost_filename = getCostMatrixFileName(vid, size, [x, center_y], [2*width_half, 2*height_half])
        
        if os.path.isfile(cost_filename):
            print("Cost matrix for center {}, {} already exists at: {}".format(x, center_y, cost_filename))
            continue
        
        print("Cost matrix does not exist yet. Building...")
        
        # Build cost matrix for this viewing direction. Go row by row for the cost matrix.
        costMatrix = np.zeros((numFrames, numFrames), dtype=np.float32)
        for i in range(costMatrix.shape[0]):
            outfile = getSATFileName(vid, size, i, args.t)
            print("Getting sat file: {}".format(outfile))
            assert os.path.isfile(outfile)
            row_of_SATs = np.load(outfile, mmap_mode='r')
            print("Loaded row {} of SATs (shape: {})".format(i, row_of_SATs.shape))
            for j in range(numFrames):
                if (i <= j):
                    SAT = np.array(row_of_SATs[j-i])
                    sqdiff = findFOVFromSAT(SAT, topLeft, botRight, size[0], size[1])
                    if sqdiff < 0:
                        print("FLOATING POINT ERROR! Square diff is: {}. Clipping to 0.".format(sqdiff))
                        sqdiff = 0
                    costMatrix[i, j] = math.sqrt(sqdiff)
                else:
                    costMatrix[i, j] = costMatrix[j, i]
                print("Processed i, j = {}, {}".format(i, j))
            del row_of_SATs
        print("Final cost matrix for Width/Height {}, {} centered at {}, {} is {}".format(2*width_half, 2*height_half, x, center_y, costMatrix))
        np.save(cost_filename, costMatrix)
        print("Saved cost matrix to: {}".format(cost_filename))

def getSumOfIntensities(SAT, topLeft, botRight):
    topLeft = topLeft - 1
    topLeft = topLeft.astype(np.int32)
    botRight = np.ceil(botRight).astype(np.int32)
    
    botRightVal = SAT[botRight[1], botRight[0]]
    topRightVal = SAT[topLeft[1], botRight[0]] if topLeft[1] >= 0 else 0
    
    topLeftVal = SAT[topLeft[1], topLeft[0]] if topLeft[0] >= 0 and topLeft[1] >= 0 else 0
    botLeftVal = SAT[botRight[1], topLeft[0]] if topLeft[0] >= 0 else 0
    return botRightVal - topRightVal - botLeftVal + topLeftVal
        
def findFOVFromSAT(SAT, topLeft, botRight, w, h):
    if (botRight[0] >= topLeft[0] and botRight[1] >= topLeft[1]):
        result = getSumOfIntensities(SAT, topLeft, botRight)
        return result
    else:
        # Split into two viewports.
        box1_br = np.array(botRight)
        box2_tl = np.array(topLeft)
        if botRight[0] < topLeft[0]:
            box1_br[0] = w - 1
            box2_tl[0] = 0
        if botRight[1] < topLeft[1]:
            box1_br[1] = h - 1
            box2_tl[1] = 0
        result = getSumOfIntensities(SAT, topLeft, box1_br) + getSumOfIntensities(SAT, box2_tl, botRight)
        return result
    
def getSATBounds(center_x, center_y, width_half, height_half, res_x, res_y):
    # return top left, bottom right window of SAT to extract.
    topLeft = np.array([center_x - width_half, center_y - height_half])
    topLeft = np.mod(topLeft, np.array([res_x, res_y]))
    if height_half == res_y / 2:
        height_half = height_half - 1
    botRight = np.array([center_x + width_half, center_y + height_half])
    botRight = np.mod(botRight, np.array([res_x, res_y]))
    return topLeft, botRight
    
def getFrames(vid, resized=None, gray=False, frameNum=None):    
    frames = []
    vidcap = cv2.VideoCapture(vid)
    success, image = vidcap.read()
    if success:
        if resized is not None:
            image = resizeFrame(image, resized[0], resized[1])
        if gray:
            image = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        else:
            image = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
        if frameNum is None:
            frames.append(image)
        else:
            if frameNum == 0:
                return image
    print("Image shape: {}. dtype: {}".format(image.shape, image.dtype))
    
    count = 1
    while success:
        success, image = vidcap.read()
        if success:
            if resized is not None:
                image = resizeFrame(image, resized[0], resized[1])
            if gray:
                image = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
            else:
                image = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
            if frameNum is None:
                frames.append(image)
            else:
                if frameNum == count:
                    return image
        count += 1
    
    frames = np.stack(frames, axis=0)
    print("Done reading frames. Shape: {}. Dtype: {}".format(frames.shape, frames.dtype))
    return frames

def resizeFrame(frame, w, h):
    return cv2.resize(frame, (w, h), interpolation=cv2.INTER_NEAREST)

def computeFrameDiff(frame1, mask1, frame2, mask2, threshold):
    finalMask = np.maximum(mask1, mask2)
    diffimg = cv2.absdiff(frame1 / 255., frame2 / 255.)  # float64
    squaredImg = np.multiply(diffimg, diffimg)  # float64
    summedImg = np.sum(squaredImg, axis=2)  # float64
    
    bitmasked = cv2.bitwise_and(summedImg, summedImg, mask=finalMask)
    if threshold is not None:
        bitmasked[bitmasked < threshold] = 0.
    masked = bitmasked
    assert masked.dtype == np.float64
    return masked

def scaleBySphericalProjection(frame, square=True):
    tmp = computeScalingMapOnEquirect(frame.shape[1], frame.shape[0])
    assert np.isclose(np.amin(tmp), 1)
    if square:
        scaleMap = np.multiply(tmp, tmp)  # float64
    else:
        scaleMap = tmp
    return np.multiply(scaleMap, frame, casting='unsafe')

def computeSummedAreaTable(frame1, mask1, frame2, mask2, threshold):
    # Save numpy array to file.
    frameDiff = computeFrameDiff(frame1, mask1, frame2, mask2, threshold)  # float64
    frameDiff = scaleBySphericalProjection(frameDiff)  # float64
    summedArea = np.zeros(frameDiff.shape, dtype=frameDiff.dtype) # float64
    assert summedArea.dtype == np.float64
    for i in range(frameDiff.shape[0]):
        for j in range(frameDiff.shape[1]):
            sumAbove = summedArea[i-1, j] if i - 1 >= 0 else 0
            sumLeft = summedArea[i, j-1] if j - 1 >= 0  else 0
            sumLeftAbove = summedArea[i - 1, j-1] if i - 1 >= 0 and j - 1 >= 0 else 0
            summedArea[i, j] = frameDiff[i, j] + sumAbove + sumLeft - sumLeftAbove
    assert summedArea.dtype == np.float64
    return summedArea.astype(np.float32)  # float32 to save space

def computeSATs(vid, vidFrames, edgeFrames, size, threshold):
    assert vidFrames.shape[0] == edgeFrames.shape[0]
        
    for i in range(vidFrames.shape[0]):
        outfile = getSATFileName(vid, size, i, threshold)
        if os.path.isfile(outfile):
            print("Row {} is already computed: {}".format(i, outfile))
            continue
        SATs = np.zeros((vidFrames.shape[0] - i, vidFrames.shape[1], vidFrames.shape[2]), dtype=np.float32)
        print("Computing row {}. SATs shape: {}. dtype: {}".format(i, SATs.shape, SATs.dtype))
        
        count = 0
        for j in range(vidFrames.shape[0]):
            if (i <= j):
                SATs[count, :, :] = computeSummedAreaTable(vidFrames[i], edgeFrames[i], vidFrames[j], edgeFrames[j], threshold)  # float32
                print("Processed i, j = {}, {}. Entered as entry {} out of {}".format(i, j, count, SATs.shape[0] - 1))
                count = count + 1
        np.save(outfile, SATs)
        print("Wrote SATs of shape {} and dtype {} to file: {}".format(SATs.shape, SATs.dtype, outfile))

def getSATFileName(vid, size, rowNum, thres):
    directory = getPreprocessDir(vid)
    basename = os.path.splitext(os.path.basename(vid))[0]
    return os.path.join(directory, "{}_{}_{}_thres_{}_row_{}.npy".format(basename, size[0], size[1], thres, rowNum))

def getCostMatrixFileName(vid, size, center, fovs):
    directory = getPreprocessDir(vid)
    cost_directory = os.path.join(directory, "costs", "size_{}_{}_fov_{}_{}".format(size[0], size[1], fovs[0], fovs[1]), "{:.3f}".format(center[1]))
    if not os.path.exists(cost_directory):
        os.makedirs(cost_directory)
    
    basename = os.path.splitext(os.path.basename(vid))[0]
    return os.path.join(cost_directory, "{}_thres_{}_center_{:.3f}_{:.3f}.npy".format(basename, args.t, center[0], center[1]))

def getNumFrames(vid):
    edges = getEdges(vid)
    return edges.shape[0]

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    #  parser.add_argument("-d", help="Directory of clips.", type=str)
    parser.add_argument("-i", help="Input equirect 360 video.", type=str, required=False)
    parser.add_argument("-d", help="Input directory of equirect 360 videos. Batch process all equirect videos in this directory.", type=str, required=False)
    parser.add_argument("-c", dest="clean", action='store_true', help="Whether or not to clean previous data")
    parser.add_argument("-a", dest="sat", action='store_true', help="Whether or not to compute sats")
    parser.add_argument("-s", help="Horizontal and vertical resolution for SAT", type=int, nargs="+", default=[640, 320])
    parser.add_argument("-m", dest="matrices", action='store_true', help="Whether or not to compute cost matrices")
    parser.add_argument("-t", help="Threshold tau (see appendix of paper). Ignores pixel differences below this threshold to avoid over-penalizing arcs with stochastic motion (e.g., moving trees). Empirically, we found that a clip with no trees in the foreground works well with tau = 0.015, whereas a clip with large foreground trees moving in the wind requires a larger tau = 0.2", type=float, required=True)
    parser.set_defaults(clean=False, matrices=False, sat=False, vertical=False)
    args = parser.parse_args()

    assert args.i or args.d, "Need to enter either -i or -d."
    
    if args.i is not None:
        assert os.path.isfile(args.i)
    if args.d is not None:
        assert os.path.exists(args.d)

    lst_of_vids = []
    if args.i is not None:
        lst_of_vids = [args.i]
    else:
        for subfolder in os.listdir(args.d):
            if not os.path.isdir(os.path.join(args.d, subfolder)):
                continue
            subdir = os.path.join(args.d, subfolder)
            for vid in os.listdir(subdir):
                if vid.endswith(".mp4") and not vid.startswith("._"):
                    lst_of_vids.append(os.path.join(subdir, vid))
    
    print("Going to preprocess the files: {}".format(lst_of_vids))

    for vid in lst_of_vids:
        preprocess_dir = getPreprocessDir(vid)

        edges_video = os.path.join(preprocess_dir, "edges_@{}.mp4".format(args.s[0]))
        if not args.clean and os.path.isfile(edges_video):
            print("Edge mask video already exists for {}".format(vid))
        else:
            if os.path.isfile(edges_video):
                os.remove(edges_video)
            compute_edge_masks(vid, args.s, 29.97, lowThreshold=80, highThreshold=100)

    if args.sat:
        for i in range(len(lst_of_vids)):
            vid = lst_of_vids[i]
            SAT_file = getSATFileName(vid, args.s, getNumFrames(vid) - 1, args.t)

            if os.path.isfile(SAT_file):
                print("SATs for {} is computed already at: {}".format(vid, SAT_file))
            else:
                print("Insufficient SATs (no {}) computed for video: {}.".format(SAT_file, vid))
                edgeFrames = getEdges(vid)
                vidFrames = getFrames(vid, resized=args.s)
                assert len(edgeFrames) == len(vidFrames)
                print("Using tau threshold {}".format(args.t))
                SATs = computeSATs(vid, vidFrames, edgeFrames, args.s, args.t)
            
    if args.matrices:
        for i in range(len(lst_of_vids)):
            vid = lst_of_vids[i]
            generateCostMatrices(vid, (int)(args.s[1] / 2), args.s)
