using Data;
using STRAT.Client.Sequence;
using STRAT.Client.Tutorial;
using STRAT.Starter;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using STRAT.UI.MainIconGroup;
using UnityEngine;

namespace STRAT
{
    public class SequenceControllerRoutine
    {
        public int NodeOrder { get; set; } = 0;
        public int NextNodeOrder { get; set; } = 0;

        public List<SequenceNodeBase> NodeList { get; } = new();
        private readonly Dictionary<int, bool> _touchBlockList = new();
        public SequenceNodeBase CurrentNode { get; private set; } = null;

        public Action<int> OnNodeComplete { get; set; }
        public Action OnFinish { get; set; }
        public Action<int> OnNodePlay { get; set; }

        public void AddNode(int kind, SequenceData data, bool touchBlock, bool group = false)
        {
            NodeList.Add(SequenceManagerExtension.CreateNode(kind, data));
            _touchBlockList.TryAdd(kind, touchBlock);
        }

        public void Clear()
        {
            NodeList.Clear();
            _touchBlockList.Clear();
            OnNodePlay = null;
            OnFinish = null;
            OnNodeComplete = null;
        }

        public IEnumerator PlayNodeCoroutine(SequenceCategory category)
        {
            while (true)
            {
                NodeOrder = NextNodeOrder;
                if (NodeOrder >= NodeList.Count)
                {
                    OnFinish?.Invoke();
                    yield break;
                }

                CurrentNode = GetCurrentNode();
                if (CurrentNode == null)
                {
                    Debug.LogError("Current node is null. Cannot play sequence.");
                    yield break;
                }

                if (CurrentNode.GetState() == SequenceNodeBase.State.Playing)
                {
                    Debug.LogWarning("Sequence is already playing.");
                    yield break;
                }

                SequenceManagerExtension.ClearingDlg(CurrentNode);
                CurrentNode.Play();
                OnNodePlay?.Invoke(CurrentNode.Kind);
                UpdateTouchBlock(CurrentNode.Kind);
                SequenceManagerExtension.PrintLog(category, CurrentNode, CurrentNode.Kind, out var log);

                TutorialManager.Instance.ActiveInputIdleShortcut = GetIsActiveInputIdleShortcut(CurrentNode.Data.Type);

                while (CurrentNode.GetPlaying())
                {
                    yield return null;
                }

                if (CurrentNode.GetPause())
                {
                    Debug.LogWarning("Sequence Pause or Stop");
                    yield break;
                }

                if (CurrentNode.GetState() == SequenceNodeBase.State.Error)
                {
                    LoginProcessor.Instance.ExitToTitleOnConnectionLost();
                    yield break;
                }

                OnNodeComplete?.Invoke(CurrentNode.Kind);

                SetNextNodeOrder();

                if (TutorialUtils.IsShopPopupMissionIconEnabled())
                {
                    MainIconGroup.UpdateIcons();
                }

                yield return null;
            }
        }

        private void SetNextNodeOrder()
        {
            NextNodeOrder = NodeOrder + 1;

            if (CurrentNode.Data.Type == SequenceNodeType.CheckTutorialConditionData)
            {
                if (CurrentNode is SequenceNodeCheckLocalCacheData checkLocalCacheData)
                {
                    int kind = checkLocalCacheData.GetGotoKind();
                    if (kind > 0)
                    {
                        var order = NodeList.FindIndex(x => x.Kind == kind);
                        if (order >= 0) NextNodeOrder = order;
                    }
                }
            }

            if (CurrentNode.Data.Skip == SkipType.Auto)
            {
                var order = NodeList.FindIndex(x => x.Kind == CurrentNode.Data.SkipPoint);
                if (order >= 0) NextNodeOrder = order;
            }
        }

        private bool GetIsActiveInputIdleShortcut(SequenceNodeType type)
        {
            return type == SequenceNodeType.WaitDlg ||
                   type == SequenceNodeType.WaitEventOnClick ||
                   type == SequenceNodeType.Condition ||
                   type == SequenceNodeType.WaitStageChange;
        }

        private SequenceNodeBase GetCurrentNode()
        {
            if (NodeOrder < 0 || NodeOrder >= NodeList.Count) return null;
            if (NodeList.Count == 0) return null;
            return NodeList[NodeOrder];
        }

        private void UpdateTouchBlock(int kind)
        {
            bool active = false;
            if (_touchBlockList != null && _touchBlockList.Count > 0)
            {
                _touchBlockList.TryGetValue(kind, out active);
            }
            SequenceManager.ActiveTouchBlock(active, $"category: , kind:{kind}");
        }
    }
}