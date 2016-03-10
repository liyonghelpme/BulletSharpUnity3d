﻿using System;
using UnityEngine;
using System.Collections;
using BulletSharp;
using BulletSharp.SoftBody;
using System.Collections.Generic;
using BulletUnity.Debugging;


namespace BulletUnity {
    public class BPhysicsWorld : MonoBehaviour, IDisposable {

        public enum WorldType
        {
            CollisionOnly,
            RigidBodyDynamics,
            MultiBodyWorld, //for FeatherStone forward dynamics I think
            SoftBodyAndRigidBody,
        }

        public enum CollisionConfType
        {
            DefaultDynamicsWorldCollisionConf,
            SoftBodyRigidBodyCollisionConf,
        }

        public enum BroadphaseType
        {
            DynamicAABBBroadphase,
            Axis3SweepBroadphase,
            Axis3SweepBroadphase_32bit,
            SimpleBroadphase,
        }

        protected static BPhysicsWorld singleton;
        protected static bool _isDisposed = false;
        const int axis3SweepMaxProxies = 32766;

        public static BPhysicsWorld Get()
        {
            if (singleton == null && !_isDisposed)
            {
                BPhysicsWorld[] ws = FindObjectsOfType<BPhysicsWorld>();
                if (ws.Length == 1)
                {
                    singleton = ws[0];
                }
                else if (ws.Length == 0)
                {
					Debug.LogError("Need to add a dynamics world to the scene");
                }
                else {
					Debug.LogError("Found more than one dynamics world.");
                    singleton = ws[0];
                    for (int i = 1; i < ws.Length; i++)
                    {
                        GameObject.Destroy(ws[i].gameObject);
                    }
                }
            }
            if (singleton.m_world == null && !singleton.isDisposed) singleton._InitializePhysicsWorld();
            return singleton;
        }

        [SerializeField]
        protected DebugDrawModes _debugDrawMode = DebugDrawModes.DrawWireframe;
        public DebugDrawModes DebugDrawMode
        {
            get { return _debugDrawMode; }
            set
            {
                _debugDrawMode = value;
                if (_doDebugDraw && m_world != null && m_world.DebugDrawer != null)
                {
                    m_world.DebugDrawer.DebugMode = value;
                }
            }
        }

        [SerializeField]
        protected bool _doDebugDraw = false;
        public bool DoDebugDraw
        {
            get { return _doDebugDraw; }
            set
            {
                if (_doDebugDraw != value && m_world != null)
                {
                    if (value == true)
                    {
                        DebugDrawUnity db = new DebugDrawUnity();
                        db.DebugMode = _debugDrawMode;
                        m_world.DebugDrawer = db;
                    }
                    else {
                        IDebugDraw db = m_world.DebugDrawer;
                        if (db != null && db is IDisposable)
                        {
                            ((IDisposable)db).Dispose();
                        }
                        m_world.DebugDrawer = null;
                    }
                }
                _doDebugDraw = value;
            }
        }

        [SerializeField]
        WorldType m_worldType = WorldType.RigidBodyDynamics;
        public WorldType worldType
        {
            get { return m_worldType; }
            set {
                if (value != m_worldType && m_world != null)
                {
                    Debug.LogError("Can't modify a Physics World after simulation has started");
                    return;
                }
                m_worldType = value;
            }
        }

        [SerializeField]
        CollisionConfType m_collisionType = CollisionConfType.DefaultDynamicsWorldCollisionConf;
        public CollisionConfType collisionType {
            get { return m_collisionType; }
            set {
                if (value != m_collisionType && m_world != null)
                {
					BDebug.LogError("Can't modify a Physics World after simulation has started", debugType);
                    return;
                }
                m_collisionType = value; }
        }

        [SerializeField]
        BroadphaseType m_broadphaseType = BroadphaseType.DynamicAABBBroadphase;
        public BroadphaseType broadphaseType
        {
            get { return m_broadphaseType; }
            set {
                if (value != m_broadphaseType && m_world != null)
                {
					BDebug.LogError("Can't modify a Physics World after simulation has started", debugType);
                    return;
                }
                m_broadphaseType = value; }
        }

        [SerializeField]
        Vector3 m_axis3SweepBroadphaseMin = new Vector3(-1000f, -1000f, -1000f);
        public Vector3 axis3SweepBroadphaseMin
        {
            get { return m_axis3SweepBroadphaseMin; }
            set {
                if (value != m_axis3SweepBroadphaseMin && m_world != null)
                {
					BDebug.LogError("Can't modify a Physics World after simulation has started", debugType);
                    return;
                }
                m_axis3SweepBroadphaseMin = value; }
        }

        [SerializeField]
        Vector3 m_axis3SweepBroadphaseMax = new Vector3(1000f, 1000f, 1000f);
        public Vector3 axis3SweepBroadphaseMax
        {
            get { return m_axis3SweepBroadphaseMax; }
            set {
                if (value != m_axis3SweepBroadphaseMax && m_world != null)
                {
					BDebug.LogError("Can't modify a Physics World after simulation has started", debugType);
                    return;
                }
                m_axis3SweepBroadphaseMax = value; }
        }

        [SerializeField]
        Vector3 m_gravity = new Vector3(0f, -9.8f, 0f);
        public Vector3 gravity
        {
            get { return m_gravity; }
            set {
                if (_ddWorld != null)
                {
                    BulletSharp.Math.Vector3 grav = value.ToBullet();
                    _ddWorld.SetGravity(ref grav);
                }
                m_gravity = value; }
        }

		public BDebug.DebugType debugType;

        /*
        [SerializeField]
        bool m_doCollisionCallbacks = true;
        public bool doCollisionCallbacks
        {
            get { return m_doCollisionCallbacks; }
            set { m_doCollisionCallbacks = value; }
        }
        */

        CollisionConfiguration CollisionConf;
        CollisionDispatcher Dispatcher;
        BroadphaseInterface Broadphase;
        SoftBodyWorldInfo softBodyWorldInfo;
        SequentialImpulseConstraintSolver Solver;
        GhostPairCallback ghostPairCallback = null;

        CollisionWorld m_world;
        public CollisionWorld world
        {
            get { return m_world; }
            set { m_world = value; }
        }

        private DiscreteDynamicsWorld _ddWorld; // convenience variable so we arn't typecasting all the time.

        protected int _frameCount;
        public int frameCount
        {
            get
            {
                return _frameCount;
            }
        }

        BDefaultCollisionHandler collisionEventHandler = new BDefaultCollisionHandler();
        public void RegisterCollisionCallbackListener(BCollisionObject.BICollisionCallbackEventHandler toBeAdded)
        {
            if (collisionEventHandler != null) collisionEventHandler.RegisterCollisionCallbackListener(toBeAdded);
        }

        public void DeregisterCollisionCallbackListener(BCollisionObject.BICollisionCallbackEventHandler toBeRemoved)
        {
            if (collisionEventHandler != null) collisionEventHandler.DeregisterCollisionCallbackListener(toBeRemoved);
        }

        public void OnDrawGizmos()
        {
            if (_doDebugDraw && m_world != null)
            {
                m_world.DebugDrawWorld();
            }
        }

        //It is critical that Awake be called before any other scripts call BPhysicsWorld.Get()
        //Set this script and any derived classes very early in script execution order.
        protected virtual void Awake()
        {
            _frameCount = 0;
            _isDisposed = false;
            singleton = BPhysicsWorld.Get();
        }

        protected virtual void FixedUpdate()
        {
            _frameCount++;
            if (_ddWorld != null)
            {
                _ddWorld.StepSimulation(UnityEngine.Time.fixedTime);
            }

            //collisions
            if (collisionEventHandler != null)
            {
                collisionEventHandler.OnPhysicsStep(world);
            }
        }

        protected virtual void OnDestroy()
        {
			BDebug.Log("Destroying Physics World", debugType);
            Dispose(false);
        }

        public bool isDisposed
        {
            get { return _isDisposed; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public bool AddAction(IAction action)
        {
            if (!_isDisposed)
            {
                if (m_worldType < WorldType.RigidBodyDynamics)
                {
                    Debug.LogError("World type must not be collision only");
                } else
                {
                    ((DynamicsWorld)world).AddAction(action);
                }
                return true;
            }
            return false;
        }

        public void RemoveAction(IAction action)
        {
            if (!_isDisposed)
            {
                if (m_worldType < WorldType.RigidBodyDynamics)
                {
					BDebug.LogError("World type must not be collision only", debugType);
                }
                ((DiscreteDynamicsWorld)m_world).RemoveAction(action);
            }
        }

        public bool AddCollisionObject(BCollisionObject co)
        {
            if (!_isDisposed)
            {
				BDebug.Log(string.Format("Adding collision object {0} to world", co), debugType);
                if (co._BuildCollisionObject())
                {
                    m_world.AddCollisionObject(co.GetCollisionObject(), co.m_groupsIBelongTo, co.m_collisionMask);
                    if (ghostPairCallback == null && co is BGhostObject && world is DynamicsWorld)
                    {
                        ghostPairCallback = new GhostPairCallback();
                        ((DynamicsWorld)world).PairCache.SetInternalGhostPairCallback(ghostPairCallback);
                    }
                    if (co is BCharacterController && world is DynamicsWorld)
                    {
                        AddAction(((BCharacterController)co).GetKinematicCharacterController());
                    }
                }
                return true;
            }
            return false;
        }

        public void RemoveCollisionObject(BulletSharp.CollisionObject co)
        {
            if (!_isDisposed)
            {
				BDebug.Log(string.Format("Removing collisionObject {0} from world", co), debugType);
                m_world.RemoveCollisionObject(co);
                //TODO handle removing kinematic character controller action
            }
        }

        public bool AddRigidBody(BRigidBody rb)
        {
            if (!_isDisposed)
            {
                if (m_worldType < WorldType.RigidBodyDynamics)
                {
					BDebug.LogError("World type must not be collision only", debugType);
                }
				BDebug.Log(string.Format("Adding rigidbody {0} to world", rb), debugType);
                if (rb._BuildCollisionObject())
                {
                    ((DiscreteDynamicsWorld)m_world).AddRigidBody((RigidBody)rb.GetCollisionObject(), rb.m_groupsIBelongTo, rb.m_collisionMask);
                }
                return true;
            }
            return false;
        }

        public void RemoveRigidBody(BulletSharp.RigidBody rb)
        {
            if (!_isDisposed)
            {
                if (m_worldType < WorldType.RigidBodyDynamics)
                {
					BDebug.LogError("World type must not be collision only", debugType);
                }
				BDebug.Log(string.Format("Removing rigidbody {0} from world", rb), debugType);
                ((DiscreteDynamicsWorld)m_world).RemoveRigidBody(rb);
            }
        }

        public bool AddConstraint(BTypedConstraint c)
        {
            if (!_isDisposed)
            {
                if (m_worldType < WorldType.RigidBodyDynamics)
                {
					BDebug.LogError("World type must not be collision only", debugType);
                }
				BDebug.Log(string.Format("Adding constraint {0} to world", c), debugType);
                if (c._BuildConstraint())
                {
                    ((DiscreteDynamicsWorld)m_world).AddConstraint(c.GetConstraint(), c.disableCollisionsBetweenConstrainedBodies);
                }
                return true;
            }
            return false;
        }

        public void RemoveConstraint(BulletSharp.TypedConstraint c)
        {
            if (!_isDisposed)
            {
                if (m_worldType < WorldType.RigidBodyDynamics)
                {
					BDebug.LogError("World type must not be collision only", debugType);
                }
				BDebug.Log(string.Format("Removing constraint {0} from world", c), debugType);
                ((DiscreteDynamicsWorld)m_world).RemoveConstraint(c);
            }
        }

        public bool AddSoftBody(BSoftBody softBody)
        {
            if (!(m_world is BulletSharp.SoftBody.SoftRigidDynamicsWorld))
            {
				BDebug.LogError("The Physics World must be a BSoftBodyWorld for adding soft bodies", debugType);
                return false;
            }
            if (!_isDisposed)
            {
				BDebug.Log(string.Format("Adding softbody {0} to world", softBody), debugType);
                if (softBody._BuildCollisionObject())
                {
                    ((BulletSharp.SoftBody.SoftRigidDynamicsWorld)m_world).AddSoftBody((SoftBody)softBody.GetCollisionObject());
                }
                return true;
            }
            return false;
        }

        public void RemoveSoftBody(BulletSharp.SoftBody.SoftBody softBody)
        {
            if (!_isDisposed && m_world is BulletSharp.SoftBody.SoftRigidDynamicsWorld)
            {
				BDebug.Log(string.Format("Removing softbody {0} from world", softBody), debugType);
                ((BulletSharp.SoftBody.SoftRigidDynamicsWorld)m_world).RemoveSoftBody(softBody);
            }
        }

        protected virtual void _InitializePhysicsWorld() {
            _isDisposed = false;
            if (m_worldType == WorldType.SoftBodyAndRigidBody && m_collisionType == CollisionConfType.DefaultDynamicsWorldCollisionConf)
            {
				BDebug.LogError("For World Type = SoftBodyAndRigidBody collisionType must be collisionType=SoftBodyRigidBodyCollisionConf. Switching", debugType);
                m_collisionType = CollisionConfType.SoftBodyRigidBodyCollisionConf;
            }

            if (m_collisionType == CollisionConfType.DefaultDynamicsWorldCollisionConf)
            {
                CollisionConf = new DefaultCollisionConfiguration();
            } else if (m_collisionType == CollisionConfType.SoftBodyRigidBodyCollisionConf)
            {
                CollisionConf = new SoftBodyRigidBodyCollisionConfiguration();
            }

            Dispatcher = new CollisionDispatcher(CollisionConf);

            if (m_broadphaseType == BroadphaseType.DynamicAABBBroadphase)
            {
                Broadphase = new DbvtBroadphase();
            } else if (m_broadphaseType == BroadphaseType.Axis3SweepBroadphase)
            {
                Broadphase = new AxisSweep3(m_axis3SweepBroadphaseMin.ToBullet(), m_axis3SweepBroadphaseMax.ToBullet(), axis3SweepMaxProxies);
            } else if (m_broadphaseType == BroadphaseType.Axis3SweepBroadphase_32bit)
            {
                Broadphase = new AxisSweep3_32Bit(m_axis3SweepBroadphaseMin.ToBullet(), m_axis3SweepBroadphaseMax.ToBullet(), axis3SweepMaxProxies);
            } else
            {
                Broadphase = null;
            }

            if (m_worldType == WorldType.CollisionOnly)
            {
                m_world = new CollisionWorld(Dispatcher, Broadphase, CollisionConf);
                _ddWorld = null;
            }
            else if (m_worldType == WorldType.RigidBodyDynamics)
            {
                m_world = new DiscreteDynamicsWorld(Dispatcher, Broadphase, null, CollisionConf);
                _ddWorld = (DiscreteDynamicsWorld)m_world;
            }
            else if (m_worldType == WorldType.MultiBodyWorld)
            {
                m_world = new MultiBodyDynamicsWorld(Dispatcher, Broadphase, null, CollisionConf);
                _ddWorld = (DiscreteDynamicsWorld)m_world;
            }
            else if (m_worldType == WorldType.SoftBodyAndRigidBody)
            {
                Solver = new SequentialImpulseConstraintSolver();

                softBodyWorldInfo = new SoftBodyWorldInfo
                {
                    AirDensity = 1.2f,
                    WaterDensity = 0,
                    WaterOffset = 0,
                    WaterNormal = BulletSharp.Math.Vector3.Zero,
                    Gravity = UnityEngine.Physics.gravity.ToBullet(),
                    Dispatcher = Dispatcher,
                    Broadphase = Broadphase
                };
                softBodyWorldInfo.SparseSdf.Initialize();

                m_world = new SoftRigidDynamicsWorld(Dispatcher, Broadphase, Solver, CollisionConf);
                _ddWorld = (DiscreteDynamicsWorld)m_world;

                m_world.DispatchInfo.EnableSpu = true;
                softBodyWorldInfo.SparseSdf.Reset();
                softBodyWorldInfo.AirDensity = 1.2f;
                softBodyWorldInfo.WaterDensity = 0;
                softBodyWorldInfo.WaterOffset = 0;
                softBodyWorldInfo.WaterNormal = BulletSharp.Math.Vector3.Zero;
                softBodyWorldInfo.Gravity = m_gravity.ToBullet();
            }
            if (_ddWorld != null)
            {
                _ddWorld.Gravity = m_gravity.ToBullet();
            }
            if (_doDebugDraw) {
                DebugDrawUnity db = new DebugDrawUnity();
                db.DebugMode = _debugDrawMode;
                m_world.DebugDrawer = db;
            }
        }

        protected void Dispose(bool disposing) {
			BDebug.Log("BDynamicsWorld Disposing physics.", debugType);

            if (m_world != null) {
                //remove/dispose constraints
                int i;
                if (_ddWorld != null)
                {
                    for (i = _ddWorld.NumConstraints - 1; i >= 0; i--)
                    {
                        TypedConstraint constraint = _ddWorld.GetConstraint(i);
                        _ddWorld.RemoveConstraint(constraint);
                        constraint.Dispose();
                    }
                }

                //remove the rigidbodies from the dynamics world and delete them
                for (i = m_world.NumCollisionObjects - 1; i >= 0; i--) {
                    CollisionObject obj = m_world.CollisionObjectArray[i];
                    RigidBody body = obj as RigidBody;
                    if (body != null && body.MotionState != null) {
                        body.MotionState.Dispose();
                    }
                    m_world.RemoveCollisionObject(obj);
                    obj.Dispose();
                }

                if (m_world.DebugDrawer != null) {
                    if (m_world.DebugDrawer is IDisposable) {
                        IDisposable dis = (IDisposable)m_world.DebugDrawer;
                        dis.Dispose();
                    }
                }

                //delete collision shapes
                //foreach (CollisionShape shape in CollisionShapes)
                //    shape.Dispose();
                //CollisionShapes.Clear();

                m_world.Dispose();
                Broadphase.Dispose();
                Dispatcher.Dispose();
                CollisionConf.Dispose();
                _ddWorld = null;
                m_world = null;
            }

            if (Broadphase != null) {
                Broadphase.Dispose();
                Broadphase = null;
            }
            if (Dispatcher != null) {
                Dispatcher.Dispose();
                Dispatcher = null;
            }
            if (CollisionConf != null) {
                CollisionConf.Dispose();
                CollisionConf = null;
            }
            if (Solver != null)
            {
                Solver.Dispose();
                Solver = null;
            }
            if (softBodyWorldInfo != null)
            {
                softBodyWorldInfo.Dispose();
                softBodyWorldInfo = null;
            }
            _isDisposed = true;
        }
    }

    public class BDefaultCollisionHandler
    {
        HashSet<BCollisionObject.BICollisionCallbackEventHandler> collisionCallbackListeners = new HashSet<BCollisionObject.BICollisionCallbackEventHandler>();

        public void RegisterCollisionCallbackListener(BCollisionObject.BICollisionCallbackEventHandler toBeAdded)
        {
            collisionCallbackListeners.Add(toBeAdded);
        }

        public void DeregisterCollisionCallbackListener(BCollisionObject.BICollisionCallbackEventHandler toBeRemoved)
        {
            collisionCallbackListeners.Remove(toBeRemoved);
        }

        public void OnPhysicsStep(CollisionWorld world)
        {
            Dispatcher dispatcher = world.Dispatcher;
            int numManifolds = dispatcher.NumManifolds;
            for (int i = 0; i < numManifolds; i++)
            {
                PersistentManifold contactManifold = dispatcher.GetManifoldByIndexInternal(i);
                CollisionObject a = contactManifold.Body0;
                CollisionObject b = contactManifold.Body1;
                if (a is CollisionObject && a.UserObject is BCollisionObject && ((BCollisionObject)a.UserObject).collisionCallbackEventHandler != null)
                {
                    ((BCollisionObject)a.UserObject).collisionCallbackEventHandler.OnVisitPersistentManifold(contactManifold);
                }
                if (b is CollisionObject && b.UserObject is BCollisionObject && ((BCollisionObject)b.UserObject).collisionCallbackEventHandler != null)
                {
                    ((BCollisionObject)b.UserObject).collisionCallbackEventHandler.OnVisitPersistentManifold(contactManifold);
                }
            }
            foreach (BCollisionObject.BICollisionCallbackEventHandler coeh in collisionCallbackListeners)
            {
                if (coeh != null) coeh.OnFinishedVisitingManifolds();
            }
        }
    }

    /*
    public class BDefaultCollisionHandler{
        public struct CollisionPair
        {
            public int obj0ID;
            public int obj1ID;

            public CollisionPair(CollisionObject a, CollisionObject b)
            {
                int idA = a.BroadphaseHandle.UniqueId;
                int idB = b.BroadphaseHandle.UniqueId;
                if (idA < idB) {
                    obj0ID = idA;
                    obj1ID = idB;
                } else
                {
                    obj0ID = idB;
                    obj1ID = idB;
                }
            }

            public override int GetHashCode()
            {
                return obj0ID ^ obj1ID;
            }

            public override bool Equals(object obj)
            {
                if (obj is CollisionPair) return false;
                else
                {
                    if (((CollisionPair) obj).obj0ID == obj0ID &&
                        ((CollisionPair) obj).obj1ID == obj1ID)
                    {
                        return true;
                    }
                    return false;
                }
            }
        }

        public class ManifoldPoints
        {
            public int frameLastVisited = 0;
            public List<PersistentManifold> manifolds = List<PersistentManifold>();
        }

        CollisionWorld m_world;
        Dictionary<CollisionPair, ManifoldPoints> pair2ManifoldMap = new Dictionary<CollisionPair, List<ManifoldPoints>>();

        public void OnPhysicsStep()
        {
            int numManifolds = m_world.Dispatcher.NumManifolds;
            // collect manifolds
            pair2ManifoldMap.Clear();
            for (int i = 0; i < numManifolds; i++)
            {
                PersistentManifold contactManifold = m_world.Dispatcher.GetManifoldByIndexInternal(i);
                CollisionObject a = contactManifold.Body0;
                CollisionObject b = contactManifold.Body1;
                bool hasCallback = false;
                if (a is CollisionObject && a.UserObject is BCollisionObject && ((BCollisionObject)a.UserObject).onCollisionCallback != null){
                    hasCallback = true;
                }
                if (b is CollisionObject && b.UserObject is BCollisionObject && ((BCollisionObject)b.UserObject).onCollisionCallback != null)
                {
                    hasCallback = true;
                }
                if (hasCallback)
                {
                    CollisionPair pair = new CollisionPair(contactManifold.Body0, contactManifold.Body1);
                    ManifoldPoints pms;
                    if (!pair2ManifoldMap.TryGetValue(pair, out pms))
                    {
                        //TODO implement pool
                        pms = new ManifoldPoints();
                        pair2ManifoldMap.Add(pair, pms);
                    }
                    pms.Add(contactManifold);  
                }
            }

            //second call the callbacks
            foreach (CollisionPair p in pair2ManifoldMap.Keys)
            {
                List<PersistentManifold> pms = pair2ManifoldMap[p];
                for (int i = 0; i < pms.Count; i++)
                {

                    CollisionObject a = pms[i].Body0;
                    CollisionObject b = pms[i].Body1;
                    if (a is CollisionObject && a.UserObject is BCollisionObject && ((BCollisionObject)a.UserObject).onCollisionCallback != null)
                    {
                        ((BCollisionObject)a.UserObject).onCollisionCallback(pms[i]);
                    }
                    if (b is CollisionObject && b.UserObject is BCollisionObject && ((BCollisionObject)b.UserObject).onCollisionCallback != null)
                    {
                        ((BCollisionObject)b.UserObject).onCollisionCallback(pms[i]);
                    }
                }
            }
        }
    }
    */
}
