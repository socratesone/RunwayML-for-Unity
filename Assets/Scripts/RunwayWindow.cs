﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using EditorCoroutines;
using DefaultableDictionary;
using System.IO;

public class RunwayWindow : EditorWindow
{
  string[] runLocations = new string[] { "Remote", "Local" };
  private bool showAdvancedOptions = false;
  private bool isRunwayRunning = false;
  private bool continuousInference = false;
  private Model[] availableModels;
  private ModelSession runningSession;
  private int selectedModelIndex = 0;
  private int runLocationIndex = 0;
  private bool isMakingRequest = false;
  private bool isProcessingInput = false;
  private bool isRetrievingModels = false;
  private IDictionary<int, int> optionSelectionIndices;
  private IDictionary<int, int> inputSourceSelectionIndices;
  private IDictionary<int, int> inputWidths;
  private IDictionary<int, int> inputHeights;
  private IDictionary<int, int> maxWidths;
  private IDictionary<int, int> maxHeights;
  private string selectedLabel = null;

  private IDictionary<int, RunwayPreviewWindow> inputWindows;
  private RunwayPreviewWindow outputWindow;
  private IDictionary<string, object> inputData;
  private Texture2D lastOutput;
  private bool isRecording;
  private string recordingKey;
  private string recordingPath;
  private int recordingFps;
  private int recordedNumberOfFrames;

  private bool isWindowEnabled = false;

  private Texture2D logoTexture;
  private GUIStyle horizontalStyle;
  private GUIStyle justifyCenterTextStyle;
  private GUIStyle boldTextStyle;

  public void OnEnable()
  {
    availableModels = new Model[0];

    inputSourceSelectionIndices = new Dictionary<int, int>().WithDefaultValue(0);
    optionSelectionIndices = new Dictionary<int, int>().WithDefaultValue(0);
    inputWidths = new Dictionary<int, int>().WithDefaultValue(640);
    inputHeights = new Dictionary<int, int>().WithDefaultValue(480);
    maxWidths = new Dictionary<int, int>().WithDefaultValue(1);
    maxHeights = new Dictionary<int, int>().WithDefaultValue(1);

    inputWindows = new Dictionary<int, RunwayPreviewWindow>();

    inputData = new Dictionary<string, object>().WithDefaultValue(null);

    isWindowEnabled = true;

    logoTexture = Resources.Load("Icons/Logo") as Texture2D;

    horizontalStyle = new GUIStyle();
    horizontalStyle.margin = new RectOffset(10, 10, 0, 0);

    justifyCenterTextStyle = new GUIStyle();
    justifyCenterTextStyle.alignment = TextAnchor.MiddleCenter;

    boldTextStyle = new GUIStyle();
    boldTextStyle.alignment = TextAnchor.MiddleCenter;
    boldTextStyle.fontStyle = FontStyle.Bold;

    this.StartCoroutine(CheckIfRunwayRunning());
    this.StartCoroutine(UpdateRunningSession());

    // this.titleContent = new GUIContent("Runway", Resources.Load("Icons/LogoDock") as Texture2D);
  }

  public void OnDisable()
  {
    isWindowEnabled = false;
  }

  [MenuItem("Window/Runway")]
  public static void ShowWindow()
  {
    GetWindow<RunwayWindow>("Runway");
  }

  private IEnumerator CheckIfRunwayRunning()
  {
    while (isWindowEnabled)
    {
      this.StartCoroutine(RunwayHub.isRunwayRunning((newStatus) =>
      {
        if (!this.isRunwayRunning && newStatus)
        {
          DiscoverModels();
        }
        this.isRunwayRunning = newStatus;
      }));
      yield return new WaitForSeconds(1);
    }
  }

  private IEnumerator DiscoverRunningSessions()
  {
    while (isWindowEnabled)
    {
      if (isRunwayRunning)
      {
        this.StartCoroutine(RunwayHub.listSessions((sessions) =>
        {
          foreach (ModelSession s in sessions)
          {
            if (s.application.Equals("Unity"))
            {
              runningSession = s;
            }
          }
        }));
      }
      yield return new WaitForSeconds(1);
    }
  }

  private void DiscoverModels()
  {
    isRetrievingModels = true;
    this.StartCoroutine(RunwayHub.listModels((models) =>
    {
      isRetrievingModels = false;
      availableModels = models;
    }));
  }

  private IEnumerator UpdateRunningSession()
  {
    while (isWindowEnabled)
    {
      if (isRunwayRunning && runningSession != null)
      {
        this.StartCoroutine(RunwayHub.getSession(runningSession.id, (session) =>
        {
          runningSession = session;
        }));
      }
      yield return new WaitForSeconds(1);
    }
  }

  private bool modelIsRunning()
  {
    return runningSession != null && runningSession.runningStatus == "RUNNING";
  }

  private bool modelIsStarting()
  {
    return runningSession != null && runningSession.runningStatus == "STARTING";
  }

  private Texture2D getSelectedTexture()
  {
    Texture2D[] textures = Selection.GetFiltered<Texture2D>(SelectionMode.Unfiltered);
    return textures.Length > 0 ? textures[0] : null;
  }

  private Display[] getAvailableDisplays()
  {
    Display[] all = Display.displays;
    List<Display> filteredDisplays = new List<Display>();
    foreach (Display d in Display.displays)
    {
      if (d.active)
      {
        filteredDisplays.Add(d);
      }
    }
    return filteredDisplays.ToArray();
  }

  private Model[] getFilteredModels()
  {
    List<Model> ret = new List<Model>();
    foreach (Model m in availableModels)
    {
      if (m.commands.Length == 0)
      {
        continue;
      }
      foreach (Field output in m.commands[0].outputs)
      {
        if (output.type == "image")
        {
          ret.Add(m);
          break;
        }
      }
    }
    return ret.ToArray();
  }

  private Texture textureForInputKey(string key, bool segmentationMap)
  {
    Texture2D texture = null;
    if (inputData[key] is Texture)
    {
      texture = inputData[key] as Texture2D;
    }
    else if (inputData[key] is GameObject)
    {
      GameObject go = ((GameObject)inputData[key]);
      Camera mainCamera = go.GetComponent<Camera>();
      if (segmentationMap)
      {
        ImageSynthesis synthesis = go.GetComponent<ImageSynthesis>();
        if (synthesis == null)
        {
          synthesis = go.AddComponent<ImageSynthesis>();
        }
        Camera cam = synthesis.capturePasses[2].camera;
        texture = RunwayUtils.CameraToTexture(cam, mainCamera.pixelWidth, mainCamera.pixelHeight);
      }
      else
      {
        texture = RunwayUtils.CameraToTexture(mainCamera, mainCamera.pixelWidth, mainCamera.pixelHeight);
      }
    }
    if (texture == null) return null;
    return texture;
  }

  private void RenderHeader()
  {
    GUILayout.BeginVertical();
    GUILayout.Space(15);
    GUILayout.EndVertical();

    GUILayout.BeginHorizontal(horizontalStyle);
    GUILayout.FlexibleSpace();
    GUILayout.Label(logoTexture, GUILayout.Width(50), GUILayout.Height(50));
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.BeginHorizontal(horizontalStyle);
    GUILayout.FlexibleSpace();
    GUIStyle titleStyle = new GUIStyle();
    titleStyle.fontSize = 20;
    GUILayout.Label("Runway", titleStyle);
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.BeginVertical();
    GUILayout.Space(15);
    GUILayout.EndVertical();
  }

  private void RenderModelInfo(Model m)
  {
    GUILayout.BeginHorizontal("box");
    GUILayout.FlexibleSpace();
    GUILayout.BeginVertical();
    GUILayout.Label("MODEL INFORMATION", boldTextStyle);
    GUILayout.Space(5);
    GUILayout.Label(m.description, justifyCenterTextStyle);
    GUILayout.Space(5);
    GUILayout.EndVertical();
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();
  }


  private void RenderRunwayNotFound()
  {
    GUILayout.BeginHorizontal("box");
    GUILayout.FlexibleSpace();
    GUILayout.BeginVertical();
    GUILayout.Space(5);
    GUILayout.Label("RUNWAY NOT FOUND", boldTextStyle);
    GUILayout.Space(5);
    GUILayout.Label("Make sure that the Runway app is running\n and that you are signed in.", justifyCenterTextStyle);
    GUILayout.Space(5);
    if (GUILayout.Button("Download Runway"))
    {
      Application.OpenURL("https://runwayml.com");
    }
    GUILayout.Space(5);
    GUILayout.EndVertical();
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();
  }

  private Dictionary<string, object> getOptions()
  {
    Model model = getSelectedModel();
    Dictionary<string, object> ret = new Dictionary<string, object>();
    for (var i = 0; i < model.options.Length; i++)
    {
      Field f = model.options[i];
      if (f.type == "file" || f.type == "category")
      {
        ret[f.name] = f.oneOf[optionSelectionIndices[i]];
      }
    }
    return ret;
  }

  private Model getSelectedModel()
  {
    return getFilteredModels().Length > 0 ? getFilteredModels()[selectedModelIndex] : null;
  }

  private void RenderModelSelection()
  {
    GUILayout.BeginHorizontal("box");
    GUILayout.BeginVertical();
    GUILayout.Space(5);
    GUILayout.Label("MODEL SELECTION", boldTextStyle);
    GUILayout.Space(5);

    GUILayout.BeginHorizontal(horizontalStyle);
    GUILayout.Label("Model");
    GUILayout.FlexibleSpace();
    string[] modelNames = new string[getFilteredModels().Length];
    for (var i = 0; i < modelNames.Length; i++) { modelNames[i] = getFilteredModels()[i].name; }
    if (selectedModelIndex >= modelNames.Length) { selectedModelIndex = 0; }
    selectedModelIndex = EditorGUILayout.Popup(selectedModelIndex, modelNames);
    GUILayout.EndHorizontal();



    GUILayout.EndVertical();
    GUILayout.EndHorizontal();
  }

  void RenderTextureInfo(Texture tex)
  {
    GUILayout.BeginVertical();
    GUILayout.Label(tex, justifyCenterTextStyle, GUILayout.MaxWidth(100), GUILayout.MaxHeight(100));
    GUILayout.Space(5);
    GUILayout.Label(System.String.Format("{0}x{1}", tex.width.ToString(), tex.height.ToString()), justifyCenterTextStyle);
    GUILayout.Space(5);
    GUILayout.EndVertical();
  }

  void RenderImageInput(Field input, int index)
  {
    GUILayout.BeginHorizontal(horizontalStyle);
    GUILayout.FlexibleSpace();

    Texture tex = textureForInputKey(input.name, false);

    if (tex != null)
    {
      RenderTextureInfo(tex);
    }
    else
    {
      GUILayout.Label("N/A");
    }
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.Space(5);

    GUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();

    if (GUILayout.Button("Select Input..."))
    {
      EditorGUIUtility.ShowObjectPicker<UnityEngine.Object>(inputData[input.name] as UnityEngine.Object, true, "t:Texture t:Camera", index);
    }

    if (Event.current.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == index)
    {
      inputData[input.name] = EditorGUIUtility.GetObjectPickerObject();
    }

    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.Space(5);

    GUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();

    if (GUILayout.Button("Open Preview"))
    {
      if (index == 0)
      {
        inputWindows[index] = GetWindow<RunwayInput1Window>("Runway - Model Input 1", true);
      }
      else
      {
        inputWindows[index] = GetWindow<RunwayInput2Window>("Runway - Model Input 2", true);
      }
    }

    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.Space(5);

    GUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();
    if (GUILayout.Button("Save Image"))
    {
      string path = EditorUtility.SaveFilePanel("Save image as PNG", "", "ModelInput.png", "png");
      byte[] data = RunwayUtils.TextureToPNG(tex as Texture2D, tex.width, tex.height);
      File.WriteAllBytes(path, data);
    }
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();


    if (inputData[input.name] != null)
    {
      if (inputWindows.ContainsKey(index))
      {
        inputWindows[index].texture = tex;
        inputWindows[index].Repaint();
      }
    }
  }

  void RenderSegmentationInput(Field input, int index)
  {
    GUILayout.BeginHorizontal(horizontalStyle);
    GUILayout.FlexibleSpace();

    Texture tex = textureForInputKey(input.name, true);

    if (inputData[input.name] != null)
    {
      RenderTextureInfo(tex);
    }
    else
    {
      GUILayout.Label("N/A");
    }
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.Space(5);

    GUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();

    if (GUILayout.Button("Select Input..."))
    {
      EditorGUIUtility.ShowObjectPicker<UnityEngine.Object>(inputData[input.name] as UnityEngine.Object, true, "t:Camera", index);
    }

    if (Event.current.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == index)
    {

      GameObject go = EditorGUIUtility.GetObjectPickerObject() as GameObject;
      inputData[input.name] = go;
      if (go != null)
      {
        ImageSynthesis synthesis = go.GetComponent<ImageSynthesis>();
        synthesis.labels = input.labels;
        synthesis.colors = input.colors;
        synthesis.defaultColor = input.defaultColor;
        inputWidths[index] = maxWidths[index] = (int)go.GetComponent<Camera>().pixelWidth;
        inputHeights[index] = maxHeights[index] = (int)go.GetComponent<Camera>().pixelHeight;
      }
    }

    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.BeginHorizontal(horizontalStyle);
    GUILayout.Label("Select Label:");
    GUILayout.FlexibleSpace();
    selectedLabel = RunwayUtils.Dropdown(selectedLabel, input.labels);
    GUILayout.EndHorizontal();

    GUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();
    using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
    {
      if (GUILayout.Button(System.String.Format("Tag Selected Objects ({0})", Selection.gameObjects.Length)))
      {
        RunwayUtils.AddTag(selectedLabel);
        foreach (GameObject go in Selection.gameObjects)
        {
          go.tag = selectedLabel;
        }
      }
    }
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();


    if (GUILayout.Button("Open Preview"))
    {
      if (index == 0)
      {
        inputWindows[index] = GetWindow<RunwayInput1Window>("Runway - Model Input 1", true);
      }
      else
      {
        inputWindows[index] = GetWindow<RunwayInput2Window>("Runway - Model Input 2", true);
      }
    }

    GUILayout.Space(5);

    GUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();
    if (GUILayout.Button("Save Image"))
    {
      string path = EditorUtility.SaveFilePanel("Save image as PNG", "", "ModelInput.png", "png");
      byte[] data = RunwayUtils.TextureToPNG(tex as Texture2D, tex.width, tex.height);
      File.WriteAllBytes(path, data);
    }
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    if (inputData[input.name] != null)
    {
      if (inputWindows.ContainsKey(index))
      {
        inputWindows[index].texture = tex;
        inputWindows[index].Repaint();
      }
    }
  }

  void RenderInputsAndOutputs()
  {
    Field[] inputs = getFilteredModels()[selectedModelIndex].commands[0].inputs;
    Field[] outputs = getFilteredModels()[selectedModelIndex].commands[0].outputs;
    for (int i = 0; i < inputs.Length; i++)
    {
      Field input = inputs[i];
      GUILayout.BeginVertical();
      GUILayout.Space(5);
      GUILayout.EndVertical();

      GUILayout.BeginHorizontal("box");
      GUILayout.BeginVertical();

      GUILayout.Space(5);

      GUILayout.BeginHorizontal(horizontalStyle);
      GUILayout.FlexibleSpace();
      GUILayout.Label(System.String.Format("Input {0}: {1} ({2})", (i + 1).ToString(), RunwayUtils.FormatFieldName(input.name), input.type), boldTextStyle);
      GUILayout.FlexibleSpace();
      GUILayout.EndHorizontal();

      GUILayout.Space(5);

      if (input.type.Equals("image"))
      {
        RenderImageInput(input, i);
      }
      else if (input.type.Equals("segmentation"))
      {
        RenderSegmentationInput(input, i);
      }
      else if (input.type.Equals("text"))
      {
        GUILayout.BeginHorizontal(horizontalStyle);
        GUILayout.Label(System.String.Format("Type {0}:", RunwayUtils.FormatFieldName(input.name)));
        GUILayout.FlexibleSpace();
        inputData[input.name] = EditorGUILayout.TextField(inputData[input.name] as string, GUILayout.MaxWidth(250));
        GUILayout.EndHorizontal();
      }
      else if (input.type.Equals("category"))
      {
        GUILayout.BeginHorizontal(horizontalStyle);
        GUILayout.Label(System.String.Format("Select {0}:", RunwayUtils.FormatFieldName(input.name)));
        GUILayout.FlexibleSpace();
        inputData[input.name] = RunwayUtils.Dropdown(inputData[input.name] as string, input.oneOf);
        GUILayout.EndHorizontal();
      }
      else if (input.type.Equals("number"))
      {
        GUILayout.BeginHorizontal(horizontalStyle);
        GUILayout.Label(System.String.Format("Select {0}:", RunwayUtils.FormatFieldName(input.name)));
        GUILayout.FlexibleSpace();
        if (RunwayUtils.IsAnInteger(input.step))
        {
          if (input.hasMin && input.hasMax && input.hasStep)
          {
            int value = inputData[input.name] is int ? (int)inputData[input.name] : (int)Convert.ToSingle(input.defaultValue);
            inputData[input.name] = EditorGUILayout.IntSlider(value, (int)input.min, (int)input.max);
          }
          else
          {
            int value = inputData[input.name] is int ? (int)inputData[input.name] : (int)Convert.ToSingle(input.defaultValue);
            inputData[input.name] = EditorGUILayout.IntField(value);
          }
        }
        else
        {
          if (input.hasMin && input.hasMax && input.hasStep)
          {
            float value = inputData[input.name] is float ? (float)inputData[input.name] : (float)Convert.ToSingle(input.defaultValue);
            inputData[input.name] = EditorGUILayout.Slider(value, input.min, input.max);
          }
          else
          {
            float value = inputData[input.name] is float ? (float)inputData[input.name] : Convert.ToSingle(input.defaultValue);
            inputData[input.name] = EditorGUILayout.FloatField(value);
          }
        }
        GUILayout.EndHorizontal();
      }
      else if (input.type.Equals("boolean"))
      {
        GUILayout.BeginHorizontal(horizontalStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(System.String.Format("Toggle {0}:", RunwayUtils.FormatFieldName(input.name)));
        bool value = inputData[input.name] is bool ? (bool)inputData[input.name] : false;
        inputData[input.name] = EditorGUILayout.Toggle(value, GUILayout.Width(20));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
      }
      GUILayout.Space(5);

      GUILayout.EndVertical();
      GUILayout.EndHorizontal();
    }

    GUILayout.BeginVertical();
    GUILayout.Space(5);
    GUILayout.EndVertical();

    GUILayout.BeginHorizontal("box");
    GUILayout.BeginVertical();

    GUILayout.Space(5);

    GUILayout.BeginHorizontal(horizontalStyle);
    GUILayout.FlexibleSpace();
    GUILayout.Label("Output", boldTextStyle);
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.Space(5);

    GUILayout.BeginHorizontal(horizontalStyle);
    GUILayout.FlexibleSpace();
    if (this.lastOutput)
    {
      RenderTextureInfo(this.lastOutput);
    }
    else
    {
      GUILayout.Label("N/A");
    }
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();

    if (GUILayout.Button("Open Preview"))
    {
      outputWindow = GetWindow<RunwayOutputWindow>("Runway - Model Output");
    }

    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.Space(5);

    GUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();
    if (this.lastOutput && GUILayout.Button("Save Image"))
    {
      string path = EditorUtility.SaveFilePanel("Save image as PNG", "", "ModelOutput.png", "png");
      byte[] data = RunwayUtils.TextureToPNG(this.lastOutput, this.lastOutput.width, this.lastOutput.height);
      File.WriteAllBytes(path, data);
    }
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    GUILayout.Space(5);

    GUILayout.EndVertical();
    GUILayout.EndHorizontal();



    if (lastOutput != null && outputWindow != null)
    {
      outputWindow.texture = lastOutput;
      outputWindow.Repaint();
    }
  }

  void RenderModelOptions()
  {
    GUILayout.BeginHorizontal("box");
    GUILayout.BeginVertical();
    GUILayout.Space(5);
    GUILayout.Label("SETUP OPTIONS", boldTextStyle);
    GUILayout.Space(5);

    Field[] options = getSelectedModel().options == null ? new Field[0] : getSelectedModel().options;

    for (var i = 0; i < options.Length; i++)
    {
      Field option = options[i];
      if (option.type == "category" || option.type == "file")
      {
        GUILayout.BeginHorizontal(horizontalStyle);
        GUILayout.Label(RunwayUtils.FormatFieldName(option.name));
        GUILayout.FlexibleSpace();
        optionSelectionIndices[i] = EditorGUILayout.Popup(optionSelectionIndices[i], option.oneOf);
        GUILayout.EndHorizontal();
      }
      GUILayout.Space(5);
    }

    GUILayout.BeginHorizontal(horizontalStyle);
    GUILayout.Label("Run Location");
    GUILayout.FlexibleSpace();
    runLocationIndex = EditorGUILayout.Popup(runLocationIndex, runLocations);
    GUILayout.EndHorizontal();
    GUILayout.Space(5);
    GUILayout.EndVertical();
    GUILayout.EndHorizontal();
  }

  void RunInference()
  {
    Field[] inputs = getSelectedModel().commands[0].inputs;
    Field[] outputs = getSelectedModel().commands[0].outputs;
    Dictionary<string, object> dataToSend = new Dictionary<string, object>();
    for (var i = 0; i < inputs.Length; i++)
    {
      Field input = inputs[i];
      object value = inputData[input.name];
      if (input.type.Equals("image"))
      {
        
        Texture2D tex = textureForInputKey(input.name, false) as Texture2D;
        byte[] data = RunwayUtils.TextureToPNG(tex, inputWidths[i], inputHeights[i]);
        dataToSend[input.name] = "data:image/png;base64," + System.Convert.ToBase64String(data);
      }
      else if (input.type.Equals("segmentation"))
      {
        Texture2D tex = textureForInputKey(input.name, true) as Texture2D;
        byte[] data = RunwayUtils.TextureToPNG(tex, inputWidths[i], inputHeights[i]);
        dataToSend[input.name] = "data:image/png;base64," + System.Convert.ToBase64String(data);
      }
      else if (input.type.Equals("vector"))
      {
        dataToSend[input.name] = RunwayUtils.RandomVector(input.length, input.samplingMean, input.samplingStd);
      }
      else
      {
        dataToSend[input.name] = value;
      }
    }
    this.StartCoroutine(RunwayHub.runInference(runningSession.url, getFilteredModels()[selectedModelIndex].commands[0].name, dataToSend, (outputData) =>
    {
      this.isProcessingInput = false;
      if (outputData == null)
      {
        EditorUtility.DisplayDialog("Inference Error", "There was an error processing this input", "OK");
        return;
      }
      for (var i = 0; i < outputs.Length; i++)
      {
        object value = outputData[outputs[i].name];
        if (outputs[i].type.Equals("image"))
        {
          string stringValue = value as string;
          int dataStartIndex = stringValue.IndexOf("base64,") + 7;
          byte[] outputImg = System.Convert.FromBase64String(((string)value).Substring(dataStartIndex));
          Texture2D tex = new Texture2D(2, 2); // Once image is loaded, texture will auto-resize
          tex.LoadImage(outputImg);
          this.lastOutput = tex;
        }
      }
      Repaint();
    }));

  }

  void RenderRunModel()
  {
    GUILayout.Space(5);
    GUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();
    GUILayout.Label("Run Continuously");
    this.continuousInference = EditorGUILayout.Toggle(this.continuousInference, GUILayout.Width(20));
    GUILayout.EndHorizontal();

    GUILayout.Space(5);
    GUILayout.BeginHorizontal(horizontalStyle);
    GUILayout.FlexibleSpace();

    if (modelIsRunning())
    {
      if (this.continuousInference && !this.isProcessingInput)
      {
        this.isProcessingInput = true;
        try
        {
          this.RunInference();
        }
        catch
        {
          this.isProcessingInput = false;
        }
      }
      using (new EditorGUI.DisabledScope(this.isProcessingInput))
      {
        if (GUILayout.Button("Process"))
        {
          this.isProcessingInput = true;
          try
          {
            this.RunInference();
          }
          catch
          {
            this.isProcessingInput = false;
          }
        }
      }
    }

    string buttonText;
    bool buttonDisabled;
    if (modelIsRunning())
    {
      buttonText = "Stop Model";
      buttonDisabled = false;
    }
    else if (modelIsStarting())
    {
      buttonText = "Starting Model...";
      buttonDisabled = true;
    }
    else
    {
      buttonText = "Start Model";
      buttonDisabled = false;
    }
    buttonDisabled = buttonDisabled || this.isMakingRequest;

    using (new EditorGUI.DisabledScope(buttonDisabled))
    {
      if (GUILayout.Button(buttonText))
      {
        if (modelIsRunning())
        {
          this.isMakingRequest = true;
          this.StartCoroutine(RunwayHub.stopModel(runningSession.id, (response) =>
          {
            this.runningSession = null;
            this.isMakingRequest = false;
            Repaint();
          }));
        }
        else
        {
          ProviderOptions providerOptions = new ProviderOptions();
          providerOptions.runLocation = runLocations[runLocationIndex];
          this.isMakingRequest = true;
          int versionId = getFilteredModels()[selectedModelIndex].defaultVersionId;
          this.StartCoroutine(RunwayHub.runModel(versionId, getOptions(), providerOptions, (error, session) =>
          {
            this.isMakingRequest = false;
            if (error != null)
            {
              EditorUtility.DisplayDialog("Error starting model", error, "OK");
              return;
            }
            this.runningSession = session;
            Repaint();
          }));
        }
      }
    }
    GUILayout.EndHorizontal();

  }

  void OnGUI()
  {
    if (isRunwayRunning && getFilteredModels().Length == 0 && !isRetrievingModels)
    {
      DiscoverModels();
    }
    RenderHeader();
    if (isRunwayRunning)
    {
      if (getSelectedModel() != null)
      {
        using (new EditorGUI.DisabledScope(modelIsRunning() || modelIsStarting()))
        {
          RenderModelSelection();
          GUILayout.Space(5);
          RenderModelOptions();
        }
        RenderInputsAndOutputs();
        RenderRunModel();
      }
    }
    else
    {
      RenderRunwayNotFound();
    }
  }

  public void OnInspectorUpdate()
  {
    Repaint();
  }

}
