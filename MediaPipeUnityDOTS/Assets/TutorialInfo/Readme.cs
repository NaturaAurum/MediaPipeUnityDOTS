using System;
using UnityEngine;
using UnityEngine.UIElements;

public class Readme : ScriptableObject
{
    public StyleSheet CommonStyle;
    public StyleSheet DarkStyle;
    public StyleSheet LightStyle;
    public Texture2D Icon;
    public string Title;
    public Section[] Sections;
    public bool LoadedLayout;

    [Serializable]
    public class Section
    {
        public string Heading, Text, LinkText, URL;
    }
}
