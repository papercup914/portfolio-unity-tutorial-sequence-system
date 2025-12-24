using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Data;
using STRAT.Client.Sequence;
using STRAT.Client.Tutorial;
using STRAT.UI;
using STRAT.UI.ShortCut;
using UnityEngine;

namespace STRAT
{
    public static class SequenceManagerExtension
    {
        private delegate T SpanParser<T>(ReadOnlySpan<char> span);

        private static readonly Dictionary<string, Type> BasicTypeByName = new()
        {
            { "bool", typeof(bool) },
            { "int", typeof(int) },
            { "float", typeof(float) },
            { "string", typeof(string) },
        };

        private static readonly Dictionary<Type, Delegate> Parsers = new()
        {
            { typeof(int), (SpanParser<int>) (s => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture)) },
            { typeof(float), (SpanParser<float>) (s => float.Parse(s, NumberStyles.Any)) },
            { typeof(double), (SpanParser<double>) (s => double.Parse(s)) },
            { typeof(bool), (SpanParser<bool>) (bool.Parse) },
            { typeof(string), (SpanParser<string>) (s => s.ToString()) }
        };

        public static T GetParam<T>(string strParam)
        {
            if (string.IsNullOrEmpty(strParam)) return default;

            ReadOnlySpan<char> src = strParam.AsSpan();
            int sep = src.IndexOf(':');
            if (sep <= 0 || sep == src.Length - 1) return default;

            ReadOnlySpan<char> typeSpan = src[..sep];
            ReadOnlySpan<char> valueSpan = src[(sep + 1)..];

            if (!BasicTypeByName.TryGetValue(typeSpan.ToString(), out var parameterType)) return default;
            if (parameterType != typeof(T)) return default;

            if (Parsers.TryGetValue(parameterType, out var del) && del is SpanParser<T> parser)
            {
                return parser(valueSpan);
            }
            throw new NotSupportedException($"Parser for {parameterType.Name} is not registered.");
        }

        public static bool OpenReflectionDlg(string dlgName, SequenceData sequenceData)
        {
            string className = $"STRAT.UI.{dlgName}";
            Type targetType = Type.GetType(className);
            if (targetType == null)
            {
                targetType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(className))
                    .FirstOrDefault(t => t != null);
            }

            if (targetType == null) return false;

            MethodInfo mi = targetType.GetMethod(
                "OpenUIBySequence",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );

            if (mi == null) return false;

            List<string> list = new List<string>();
            if (sequenceData.OpenParam1.IsNullOrEmpty() == false) list.Add(sequenceData.OpenParam1);
            if (sequenceData.OpenParam2.IsNullOrEmpty() == false) list.Add(sequenceData.OpenParam2);
            if (sequenceData.OpenParam3.IsNullOrEmpty() == false) list.Add(sequenceData.OpenParam3);
            if (sequenceData.OpenParam4.IsNullOrEmpty() == false) list.Add(sequenceData.OpenParam4);

            try
            {
                object result = mi.Invoke(null, new object[] { list.ToArray() });
            }
            catch (Exception ex)
            {
                Debug.LogErrorWithException(ex);
                return false;
            }
            return true;
        }

        public static SequenceNodeBase CreateNode(int kind, SequenceData data, bool replay = false)
        {
            switch (data.Type)
            {
                case SequenceNodeType.Stage: return new SequenceNodeStage(kind, data, replay);
                case SequenceNodeType.CutScene: return new SequenceNodeCutScene(kind, data, replay);
                case SequenceNodeType.DialogOpen: return new SequenceNodeDialogOpen(kind, data, replay);
                case SequenceNodeType.DialogClose: return new SequenceNodeDialogClose(kind, data, replay);
                case SequenceNodeType.WaitTime: return new SequenceNodeWaitTime(kind, data, replay);
                case SequenceNodeType.WaitEventOnClick: return new SequenceNodeWaitEventOnClick(kind, data, replay);
                case SequenceNodeType.ActiveComponent: return new SequenceNodeActiveComponent(kind, data, replay);
                case SequenceNodeType.TextComponent: return new SequenceNodeTextComponent(kind, data, replay);
                case SequenceNodeType.PlayAnimComponent: return new SequenceNodePlayAnimComponent(kind, data, replay);
                case SequenceNodeType.Audio: return new SequenceNodeAudio(kind, data, replay);
                case SequenceNodeType.ThenElse: return new SequenceNodeThenElse(kind, data, replay);
                case SequenceNodeType.WaitDlg: return new SequenceNodeWaitDlg(kind, data, replay);
                case SequenceNodeType.MoveCamera: return new SequenceNodeMoveCamera(kind, data, replay);
                case SequenceNodeType.TimelinePlayer: return new SequenceNodeTimeLinePlayer(kind, data, replay);
                case SequenceNodeType.PlayPuzzle: return new SequenceNodePlayPuzzle(kind, data, replay);
                case SequenceNodeType.Condition: return new SequenceNodeCondition(kind, data, replay);
                case SequenceNodeType.ShortCutGuide: return new SequenceNodeShortCutGuide(kind, data, replay);
                case SequenceNodeType.RequestQuestComplete: return new SequenceNodeRequestQuestComplete(kind, data, replay);
                case SequenceNodeType.ActiveBuildingObject: return new SequenceNodeActiveBuildingObject(kind, data, replay);
            }
            return null;
        }

        public static void PrintLog(string log, string titleColor = "yellow")
        {
            Debug.Log("radiant", $"<color={titleColor}>[SequenceManager]</color> <color=white>{log}</color>");
        }

        public static void PrintLog(SequenceCategory category, SequenceNodeBase node, int kind, out string outLog)
        {
            string maybeNonSequence = node.Data.Group > 0 ? "NonSequence" : "Sequence";
            outLog = string.Format($"[{category.Ex_ToString()}] : [{maybeNonSequence} Node Step] Kind : ({node.Kind})");
            Debug.Log("radiant", outLog);
        }

        public static void ClearingDlg(SequenceNodeBase node)
        {
            bool activeInputIdleShortcut = true;
            bool closeAllDlg = false;

            if (node.Data.Type == SequenceNodeType.TimelinePlayer)
            {
                activeInputIdleShortcut = false;
                closeAllDlg = true;
            }
            else if (node.Data.Type == SequenceNodeType.DialogOpen)
            {
                if (node is SequenceNodeDialogOpen nodeDlgOpen) activeInputIdleShortcut = false;
            }
            else if (node.Data.Type == SequenceNodeType.Stage)
            {
                if (node is SequenceNodeStage nodeStage)
                {
                    if (nodeStage.StageType != StageType.Territory && nodeStage.StageType != StageType.Field)
                    {
                        activeInputIdleShortcut = false;
                        closeAllDlg = true;
                    }
                }
            }

            if (activeInputIdleShortcut == false)
            {
                TutorialManager.Instance.ActiveInputIdleShortcut = false;
                UIShortCutGuide.Stop();
            }

            if (closeAllDlg)
            {
                UIShortCutGuide.Stop();
                UIManager.Instance.CloseAll(true);
            }
        }
    }
}