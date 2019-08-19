# View-Dependent Video Textures for 360° Video
Research Paper: https://lseancs.github.io/viewdepvrtextures/


## Run Demo: View-Dependent 360 Video Player (Unity)

### Requirements
- Windows
- Unity Pro 2018.2.18f1 (not tested on Unity Personal)
- HAP: https://hap.video/using-hap.html
- Oculus Rift

### Unity Setup:
1. Download "View-Dependent Video Textures for 360° Video - Release". Open the project in Unity.
2. Media setup:
   - Download this [video](https://drive.google.com/file/d/1QrES4P6xQAHwvfOE-uBkxeOip_orB0IS/view?usp=sharing) into Assets/StreamingAssets/
   - Convert the video into HAP format and place it in the same directory: 
   ```
   ffmpeg -i murdermystery_full.mp4 -c:v hap murdermystery_full.mov
   ```
   - Download the [audio](https://drive.google.com/file/d/1eYMh04wySfQgW-7lxu2nB-jMc3uqSazw/view?usp=sharing) into Assets/Resources/
3. Go to Unity's Asset Store:
   - Asset Store: RenderHeads AVPro Video (Windows) -> Import all (Note: license costs $150; not tested on free version)
   - Asset Store: JSON .NET For Unity -> Import All
4. Update Settings:
   - Edit -> Project Settings -> Player -> Other Settings -> Scripting Runtime Versions -> .NET 4.x or Equivalent
   - Edit -> Project Settings -> Player -> XR Settings -> Check "VR Supported". Make sure Oculus is in the list (you need to first set up Oculus with Unity)
4. Scene Setup:
   - Double-click on Scenes/MurderMystery.unity
   - Expand the scene hierarchy
   - For BOTH player1 and player2, go to Platform Specific -> Windows -> Preferred Video API (Direct Show). Your computer has to support hardware decoding to play the HAP videos.
5. XML tweaking
   - Open Assets/StreamingAssets/Editor/murdermystery.xml in a text editor
   - Find and replace "PARENT_DIR" with the parent directory of "View-Dependent Video Textures for 360° Video - Release"
6. Inspector Setup for "Sphere"
   - Click on "Sphere" in the scene hierarchy, which will bring up the Inspector panel on the right.
   - In the MultiplePlayerControl component -> Media: Set size to 2. Then, drag player1 into Element 0 and player2 into Element1.
   - Drag Resources/murdermystery_full.mp3 into Audio Source component -> Audio Clip
   - Drag "Main Camera" from scene hierarchy into "Update StereoMaterial (Script)" -> Camera
   - For "sphere360 (Instance)" -> Shader, select "Inside Sphere Blend".

### Run Demo
1. Go to Window menu -> Video Editor Window. This should bring up a window.
2. Resize the window so you can see the timeline and the headset and equirect previews.
3. Click on Open Edit, and select Assets/StreamingAssets/Editor/murdermystery.xml  You should see five clips on the timeline.
4. Click "Play" to play the video and watch the video in the Oculus headset. The current view of the headset is highlighted in pink on the timeline. Click "Pause" to pause the video. Click "Stop" to completely stop video playback. You can skip around by clicking on the timeline.
   - TIP: Make sure "player1" or "player2" is selected in the scene hierarchy when you are playing. If "Sphere" is selected, the player might lag.

#### To download more gated examples, see our project website at https://lseancs.github.io/viewdepvrtextures/


## Instructions to Generate Your Own Gated Clips

### Pre-process Video to Generate Summed Area Tables & Cost Matrices

This step pre-processes the video and generates summed area tables and cost matrices (see Appendix in paper). 
The SATs only need to generated once for a given threshold tau. If you wish to change the number of view discretization, FOV of each view, etc, you can re-generate cost matrices from the SATs.

For a description of all script parameters, run:
```
python3 preprocess.py -h
```

1. Generate Summed Area Tables (SATs) for a video in equirectangular format (takes ~3.3 hours for a 7 sec clip in 30fps):
```
python3 preprocess.py -i {EQUIRECT_VID_FILE_PATH} -t 0.015 -a
```
This will write SATs to a directory called {EQUIRECT_VID_FILE_PATH}-preprocess/

2. Generate cost matrices from the SATs:
```
python3 preprocess.py -i {EQUIRECT_VID_FILE_PATH} -t 0.015 -m
```
This will write cost matrices (.npy) to {EQUIRECT_VID_FILE_PATH}-preprocess/costs/


### Run Graph-Cut Algorithm to Generate View-Dependent Video Textures

This step takes the cost matrices (.npy) from the previous step and runs graph-cut to generate view-dependent video textures. This step should be re-run each time when the gate parameters or user-set thresholds change.

#### Install Requirements
- OpenCV: https://github.com/opencv/opencv
- Boost 1.70.0
- C++17
- cnpy: https://github.com/rogersce/cnpy
- JSON reader/writer: https://github.com/nlohmann/json

#### Setup
- Download MAXFLOW (version 3.01) source code: http://mouse.cs.uwaterloo.ca/code/maxflow-v3.01.zip
- Unzip and put all files into graphcut/
- Remove main.cpp

#### Compile (on Mac)
```
g++ viewdeptextures.cpp graph.cpp maxflow.cpp -L /usr/bin/ `pkg-config --cflags --libs opencv` -lcnpy -lz -l boost_program_options -l boost_system -lboost_filesystem -o main --std=c++17
```
#### To see options
```
./main --help
```
#### Example command:
```
./main -I {EQUIRECT_VID_FILE_PATH}-preprocess/costs/ -G 150 --ROIstart 35 --ROIend 5 --perceptualThreshold 2500 --minLength 30 --offscreen 1 -O {OUTPUT_DIR}
```
#### To automatically find the lowest perceptual threshold such that the total cut cost is under that threshold:
```
./main -I {EQUIRECT_VID_FILE_PATH}-preprocess/costs/ -G 150 --ROIstart 35 --ROIend 5 --findThreshold 1 --minLength 30 --offscreen 1 -O {OUTPUT_DIR}
```
This will write cost matrices (.xml) and view-dependent video textures (5 files) into an output directory.


### Play via View-Dependent 360 Video Player in Unity

If you haven't yet, follow the setup instructions above to run the demo "MurderMystery" and make sure you can run the demo.

#### Setup
1. Duplicate the demo scene by right-clicking on MurderMystery.unity and selecting "Duplicate". Rename the scene.
2. Place the raw video into Assets/StreamingAssets/ and convert the video to HAP format: 
   ```
   ffmpeg -i RAW_FILE_NAME.mp4 -c:v hap HAP_FILE_NAME.mov
   ```
3. Extract the audio and place it into Assets/Resources/
   ```
   ffmpeg -i RAW_FILE_NAME.mp4 -q:a 0 -map a RAW_AUDIO_FILE_NAME.mp3
   ```
4. Move the ```{OUTPUT_DIR}``` from the graph-cut into Assets/StreamingAssets/Editor/
5. Expand the scene hierarchy. Select player1, and in the Inspector panel on the right, change the Source Path to HAP_FILE_NAME.mov  Do the same for player2. For player1 and player2, make sure: 
   - Platform Specific -> Windows -> Preferred Video API (Direct Show) is selected.
6. Select "Sphere" from the Scene Hierarchy. In the Inspector on the right, make sure:
   - There is one of each component: Transform, Octahedron Sphere, Mesh Renderer, Update Stereo Material, Audio Source, MultiplePlayerControl, Preview mode, Head Track, and sphere360. You can remove the PlayableClips and GatedClips components.
   - In MultiplePlayerControl -> Media: Set size to 2. player1 should be in Element 0, and player2 should be in Element1.
   - Drag Resources/RAW_AUDIO_FILE_NAME.mp3 into Audio Source component -> Audio Clip
   - Drag "Main Camera" from scene hierarchy into "Update StereoMaterial (Script)" -> Camera
   - For "sphere360 (Instance)" -> Shader, select "Inside Sphere Blend".
7. Go to the Windows menu -> Video Editor Window. This should bring up the editing window.
8. At the top, the file name should already be filled in the text field (i.e., HAP_FILE_NAME.mov). To add a clip in the timeline, type in the start and end time of a clip (in seconds), and click "Add clip". This generates a small window. Right click on the window title and drag to the timeline. This registers the clip on the timeline.
9. If you want the clip to be a gated clip, check "Gated Clip". Otherwise, if you want the clip to be a standard clip, do not check "Gated Clip". See instructions below for setting up a gated clip.
10. You can rearrange the order of the clips on the timeline by right-clicking and dragging.
11. You can save the edit file by clicking on "Save Edit" at the top. You MUST save the file in Assets/StreamingAssets/Editor/

#### Instructions on setting up a gated clip:
1. Set the ROI: 
   - Click "Play" and "Pause", then move the time indicator to where you want the gate time to be. The "Relative frame" is printed out on the console. Make sure it matches the gate frame you used in the graph-cut.
   - With the media still paused, click on the toggle button "Select ROI". Left-click for the top left of the ROI, and control-click for the bottom right of the ROI.
   - You can continue to adjust the ROI. Once you are satisfied, click on "OK" (same button as "Select ROI"). Then click "Stop" to stop the video playback.
2. Load Arc Costs:
   - Click on "Load Arc Costs", and select the ```{OUTPUT_DIR}``` from running the graph-cut code.
3. Make sure the "Min Arc Length" and "Perceptual Threshold" matches what you used for the graph-cut.
   - TIP: You can play around with different parameters, and the interface will highlight the frames with valid backward arcs in gray. This helps inform your decision on the parameter values if you re-run graph-cut.
4. Click "Apply Gate" and select the same ```{OUTPUT_DIR}``` from running the graph-cut code. The cut frames are highlighted in purple. Post-processed cut frames with backward arcs that do not satisfy user-set thresholds are highlighted in cyan.
5. To enable forward jumps, check "Enable Forward Jumps" and enter perceptual threshold value. If the viewer is looking at a view that satisfies the gate condition, the player will jump to the gate frame from the current frame if the arc cost is below the threshold.
6. Make sure to save frequently. Click "Play" and move around the Oculus headset to watch the video with view-dependent textures. To see all the backward arcs displayed on the timeline, you need to reload the edit file; close and reopen the editor window, click on "Open Edit", and select the saved file.
