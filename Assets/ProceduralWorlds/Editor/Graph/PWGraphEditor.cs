﻿using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEditor;
using System.Linq;
using PW;
using PW.Core;
using PW.Node;
using PW.Editor;
using UnityEngine.Profiling;

using Debug = UnityEngine.Debug;

[System.Serializable]
public partial class PWGraphEditor : PWEditorWindow
{

	//the reference to the graph in public for the AssetHandlers class
	public PWGraph				graph;

	//event masks, zones where the graph will not process events,
	//useful when you want to add a panel on the top of the graph.
	public Dictionary< int, Rect >	eventMasks = new Dictionary< int, Rect >();
	EventType					savedEventType;
	bool						restoreEvent;
	
	protected PWGraphEditorEventInfo editorEvents { get { return graph.editorEvents; } }
	
	//size of the current window, updated each frame
	protected Vector2			windowSize;

	//Is the asset saved ?
	bool						saved;
	//Is the editor on MacOS ?
	bool 						MacOS;
	//Is the command (on MacOs) or control (on other OSs) is pressed
	bool						commandOSKey { get { return (MacOS && e.command) || (!MacOS && e.control); } }
	//Process the graph at the next event pass if true
	bool						processGraphOnNextPass;

	//Layout additional windows
	protected PWGraphOptionBar			optionBar;
	protected PWGraphNodeSelectorBar	nodeSelectorBar;
	protected PWGraphSettingsBar		settingsBar;


	//custom editor events:
	//fired whe the user resize the window
	public event Action< Vector2 >	OnWindowResize;
	//fired when a graph is loaded/unloaded
	public event Action< PWGraph >	OnGraphChanged;


	public override void OnEnable()
	{
		Profiler.BeginSample("[PW] GraphEditor Enabled");
		base.OnEnable();

		//provide the MacOS bool
		MacOS = SystemInfo.operatingSystem.Contains("Mac");

		EditorApplication.playModeStateChanged += PlayModeChangeCallback;

		//clear event masks
		eventMasks.Clear();

		LoadAssets();

		Profiler.EndSample();
	}

	public override void OnGUIEnable()
	{
		LoadStyles();

		if (graph != null)
			LoadGraph(graph);
	}

	//draw the default node graph:
	public override void OnGUI()
	{
		base.OnGUI();

		if (graph == null)
		{
			RenderGraphNotFound();
			return ;
		}
		
		//update the current GUI settings storage and clear drawed popup list:
		graph.PWGUI.StartFrame(position);
		
		//set the skin for the current window
		GUI.skin = PWGUISkin;
		
		//disable events if mouse is above an eventMask Rect.
		MaskEvents();

		//profiling
		Profiler.BeginSample("[PW] Graph redering");

		Rect pos = position;
		pos.position = Vector2.zero;
		graph.zoomPanCorrection = GUIScaleUtility.BeginScale(ref pos, pos.size / 2, 1f / graph.scale, false);
		{
			//draw the background:
			RenderBackground();
	
			//manage selection:
			SelectAndDrag();
	
			//graph rendering
			RenderOrderingGroups();
			RenderLinks();
			RenderNodes();
	
			//context menu
			ContextMenu();
	
			//fill and process remaining events if there is
			ManageEvents();

			//reset events for the next frame
			editorEvents.Reset();
	
			//TODO: repanit only if needed ?
			if (e.type == EventType.Repaint)
				Repaint();
		}
		GUIScaleUtility.EndScale();

		Profiler.EndSample();
	
		//restore masked events:
		UnMaskEvents();
		
		//we process the graph if we have to
		if (processGraphOnNextPass)
		{
			graph.Process();
			processGraphOnNextPass = false;
		}
		
		//save the size of the window
		windowSize = position.size;
	}

	void PlayModeChangeCallback(PlayModeStateChange mode)
	{
		if (mode == PlayModeStateChange.EnteredEditMode)
		{
			// Debug.Log("here !");
			// graph.Process();
		}
	}

	//TODO: move elsewhere
	public void LoadGraph(string assetPath)
	{
		LoadGraph(AssetDatabase.LoadAssetAtPath< PWGraph >(assetPath));
	}

	public void LoadGraph(PWGraph graph)
	{
		this.graph = graph;

		graph.assetFilePath = AssetDatabase.GetAssetPath(graph);
		
		//attach to graph events
		graph.OnNodeAdded += OnNodeAddedCallback;
		graph.OnNodeRemoved += OnNodeRemovedCallback;
		graph.OnLinkCreated += OnLinkCreated;

		//set the skin for the node style initialization
		GUI.skin = PWGUISkin;

		//update graph in views:
		optionBar = new PWGraphOptionBar(graph);
		nodeSelectorBar = new PWGraphNodeSelectorBar(graph);
		settingsBar = new PWGraphSettingsBar(graph);
		optionBar.LoadStyles();
		nodeSelectorBar.LoadStyles();
		settingsBar.LoadStyles();

		if (!graph.initialized)
		{
			graph.Initialize();
			graph.OnEnable();
			SaveGraph();
		}

		if (OnGraphChanged != null)
			OnGraphChanged(graph);
		
		processGraphOnNextPass = true;
	}

	public void UnloadGraph()
	{
		graph.OnNodeAdded -= OnNodeAddedCallback;
		graph.OnNodeRemoved -= OnNodeRemovedCallback;
		graph.OnLinkCreated -= OnLinkCreated;

		SaveGraph();

		Resources.UnloadAsset(graph);

		if (OnGraphChanged != null)
			OnGraphChanged(null);
	}

	public override void OnDisable()
	{
		base.OnDisable();

		//destroy the graph so it's not loaded in the void.
		if (graph != null)
			UnloadGraph();
	}

	void SaveGraph()
	{
		saved = true;
		EditorUtility.SetDirty(graph);
		AssetDatabase.SaveAssets();
	}

	bool MaskEvents()
	{
		restoreEvent = false;
		savedEventType = e.type;
		
		//check if we have an event outside of the graph event masks
		if (e.type == EventType.MouseDown || e.type == EventType.ContextClick || e.isKey || e.isScrollWheel)
		{
			foreach (var eventMask in eventMasks)
			{
				if (eventMask.Value.Contains(e.mousePosition))
				{
					//if there is, we say to ignore the event and restore it later
					restoreEvent = true;
					e.type = EventType.Ignore;
					return true;
				}
			}
		}

		return false;
	}

	void RenderBackground()
	{
		float	backgroundScale = 2f;
		int		backgroundTileSize = nodeEditorBackgroundTexture.width;
		
		Rect	position = new Rect(
			graph.panPosition.x % backgroundTileSize - backgroundTileSize,
			graph.panPosition.y % backgroundTileSize - backgroundTileSize,
			maxSize.x * 10,
			maxSize.y * 10
		);

		Rect	texCoord = new Rect(
			0,
			0,
			(maxSize.x * 10 / nodeEditorBackgroundTexture.width) * backgroundScale,
			(maxSize.y * 10 / nodeEditorBackgroundTexture.height) * backgroundScale
		);
		
		GUI.DrawTextureWithTexCoords(position, nodeEditorBackgroundTexture, texCoord);
	}

	void RenderGraphNotFound()
	{
		EditorGUILayout.LabelField("Graph not found, ouble click on a graph asset file to a graph to open it");
		// graph = EditorGUILayout.ObjectField(graph, typeof(PWGraph), false) as PWGraph;
	}

	void SelectAndDrag()
	{
		Profiler.BeginSample("[PW] Select and drag");

		//rendering the selection rect
		if (editorEvents.isSelecting)
		{
			Rect posiviteSelectionRect = PWUtils.CreateRect(e.mousePosition, editorEvents.selectionStartPoint);
			Rect decaledSelectionRect = PWUtils.DecalRect(posiviteSelectionRect, -graph.panPosition);

			//draw selection rect
			if (e.type == EventType.Repaint)
				selectionStyle.Draw(posiviteSelectionRect, false, false, false, false);

			//iterate throw all nodes of the graph and check if the selection overlaps
			graph.nodes.ForEach(n => n.isSelected = decaledSelectionRect.Overlaps(n.rect));
			editorEvents.selectedNodeCount = graph.nodes.Count(n => n.isSelected);
		}

		//multiple window drag:
		if (e.type == EventType.MouseDrag && editorEvents.isDraggingSelectedNodes)
		{
				graph.nodes.ForEach(n => {
				if (n.isSelected)
					n.rect.position += e.delta;
				});
		}

		Profiler.EndSample();
	}

	void RenderOrderingGroups()
	{
		Profiler.BeginSample("[PW] Render ordering groups");

		foreach (var orderingGroup in graph.orderingGroups)
			orderingGroup.Render(graph.panPosition, position.size, ref graph.editorEvents);

		//if the mouse was not over an ordering group this frame
		if (!editorEvents.isMouseOverOrderingGroupFrame)
			editorEvents.mouseOverOrderingGroup = null;

		Profiler.EndSample();
	}

	void ManageEvents()
	{
		Profiler.BeginSample("[PW] Managing events");

		//do not process events if we are in layout / repaint
		if (e.type == EventType.Repaint || e.type == EventType.Layout)
			return ;

		//we save with the s key
		if (e.type == EventType.KeyDown && e.keyCode == KeyCode.S)
		{
			AssetDatabase.SaveAssets();
			e.Use();
		}

		//begin to darg a link if clicked on anchor and nothing else is started
		if (editorEvents.isMouseClickOnAnchor && !editorEvents.isPanning && !editorEvents.isDraggingSomething)
			StartDragLink();
		
		//click up outside of an anchor, stop dragging
		if (e.type == EventType.MouseUp && editorEvents.isDraggingLink)
			StopDragLink(false);
		
		//duplicate selected items if cmd+d
		if (commandOSKey && e.keyCode == KeyCode.D && e.type == EventType.KeyDown)
		{
			graph.nodes.ForEach(n => n.Duplicate());

			e.Use();
		}

		//graph panning
		//if the event is a drag then it has't been used before
		if (e.type == EventType.MouseDown && !editorEvents.isDraggingSomething)
			editorEvents.isPanning = true;
		
		if (editorEvents.isPanning)
		{
			//mouse middle button or left click + cmd on mac and left click + control on other OS
			if (e.button == 2 || (e.button == 0 && commandOSKey))
				graph.panPosition += e.delta;
		}
		
		//Graph selection start event 
		if (e.type == EventType.MouseDown) //if event is mouse down
		{
			if (!editorEvents.isMouseOverSomething //if mouse is not above something
				&& e.button == 0
				&& e.modifiers == EventModifiers.None)
			{
				editorEvents.selectionStartPoint = e.mousePosition;
				editorEvents.isSelecting = true;
			}
		}

		//on mouse button up
		if (e.rawType == EventType.MouseUp)
		{
			if (editorEvents.isDraggingNode)
				Undo.RecordObject(graph, "drag node");
			if (editorEvents.isPanning)
				Undo.RecordObject(graph, "graph pan");
			if (editorEvents.isDraggingOrderingGroup)
				Undo.RecordObject(graph, "ordering graph drag");
			if (GUI.changed)
				Undo.RecordObject(graph, "something changed");
			
			editorEvents.isDraggingNode = false;
			editorEvents.isDraggingOrderingGroup = false;
			editorEvents.isSelecting = false;
			editorEvents.isPanning = false;
			editorEvents.isDraggingSelectedNodes = false;
		}
		
		//esc key event:
		if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
		{
			if (editorEvents.isDraggingLink)
				StopDragLink(false);

			editorEvents.isSelecting = false;
			editorEvents.isDraggingLink = false;
			editorEvents.isDraggingNewLink = false;
		}
		
		//fire the resize event
		if (windowSize != Vector2.zero && windowSize != position.size)
			if (OnWindowResize != null)
				OnWindowResize(position.size);
		
		//zoom
		if (e.type == EventType.ScrollWheel)
		{
			graph.scale *= 1 - (e.delta.y / 40f);
			graph.scale = Mathf.Clamp(graph.scale, .2f, 2);
		}

		//undo and redo
		if (commandOSKey && e.type == EventType.KeyDown)
		{
			if (e.keyCode == KeyCode.Z)
			{
				Undo.PerformUndo();
				e.Use();
			}
			if ((e.keyCode == KeyCode.Z && e.shift) || e.keyCode == KeyCode.Y)
			{
				Undo.PerformRedo();
				e.Use();
			}
		}
		
		//must be placed at the end of the function
		//unselect all selected links and raise an event for nodes if click beside.
		if (e.type == EventType.MouseDown
				&& !editorEvents.isMouseOverAnchor
				&& !editorEvents.isMouseOverNode
				&& !editorEvents.isMouseOverLink
				&& !editorEvents.isMouseOverOrderingGroup)
		{
			graph.RaiseOnClickNowhere();

			UnselectAllLinks();
		}

		Profiler.EndSample();
	}

	void UnMaskEvents()
	{
		if (restoreEvent)
			e.type = savedEventType;
	}
}
