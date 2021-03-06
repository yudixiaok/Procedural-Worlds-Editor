﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProceduralWorlds;
using ProceduralWorlds.Core;

namespace ProceduralWorlds.Core
{
	[System.Serializable]
	public class NodeLink
	{
		//GUID to identify the node in the LinkTable
		public string			GUID;
	
		//ColorPalette of the link
		public ColorSchemeName colorSchemeName;
		//link hightlight mode 
		public LinkHighlightMode	highlight;
		//is selected ?
		public bool				selected;
	
		//anchor where the link is connected:
		[System.NonSerialized]
		//from where the link is conected, this anchor will always be an Input anchor
		public Anchor			fromAnchor;
		[System.NonSerialized]
		//this anchor will always be an Output anchor.
		public Anchor			toAnchor;
		[System.NonSerialized]
		public BaseNode			fromNode;
		[System.NonSerialized]
		public BaseNode			toNode;

		//Control id for UnityEditor to select the link:
		[System.NonSerialized]
		public int				controlId = -1;
		[System.NonSerialized]
		public bool				initialized;


		//called once (when link is created only)
		public void Initialize(Anchor fromAnchor, Anchor toAnchor)
		{
			this.fromAnchor = fromAnchor;
			this.toAnchor = toAnchor;
			fromNode = fromAnchor.nodeRef;
			toNode = toAnchor.nodeRef;
			GUID = System.Guid.NewGuid().ToString();
			initialized = true;
		}

		//this function will be called twiced, from the two linked anchors
		// and so will receive two different anchor in parameter
		public void OnAfterDeserialize(Anchor anchor)
		{
			if (anchor.anchorType == AnchorType.Output)
				fromAnchor = anchor;
			else
				toAnchor = anchor;
			
			if (fromAnchor != null)
				fromNode = fromAnchor.nodeRef;
			if (toAnchor != null)
				toNode = toAnchor.nodeRef;
			
			//update link color:
			if (fromAnchor != null)
			{
				colorSchemeName = fromAnchor.colorSchemeName;
			}
		}

		public void ResetHighlight()
		{
			if (selected)
				highlight = LinkHighlightMode.Selected;
			else
				highlight = LinkHighlightMode.None;
		}
	
		override public string ToString()
		{
			return "link from [" + fromAnchor + "] to [" + toAnchor + "], GUID: " + GUID;
		}
	}
}