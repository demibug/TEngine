using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace GameLogic
{
    public class LodMgr : SingletonBehaviour<LodMgr>
    {
        private float m_previousLodDistance = 0f;
        
        private float m_lodDistance = 0f;
        
        private Camera m_lodCamera;
        
        private List<Action> m_actions = new List<Action>();
        
        public void AddHandler(Action action)
        {
            if (!m_actions.Contains(action))
            {
                m_actions.Add(action);
            }
        }
        
        public void RemoveHandler(Action action)
        {
            if (m_actions.Contains(action))
            {
                m_actions.Remove(action);
            }
        }

        private void Handle()
        {
            for (int i = 0; i < m_actions.Count; i++)
            {
                m_actions[i]?.Invoke();
            }
        }
        
        public float GetPreviousLodDistance()
        {
            return m_previousLodDistance;
        }
        
        public float GetLodDistance()
        {
            return m_lodDistance;
        }

        public void UpdateLodDistance(Camera camera, float dist)
        {
            m_lodDistance = camera.fieldOfView * dist;
            Handle();

        }
        public void LateUpdate()
        {
            m_previousLodDistance = GetLodDistance();
        }
    }
}
