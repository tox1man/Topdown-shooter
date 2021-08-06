﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ShootingController : IUpdatable, IFixedUpdatable
{
    private float _shootInterval = 1 / Parameters.SHOOTING_RATE;
    public GameObjectView View { get; private set; }
    private BulletsController _bulletController;

    private Rigidbody[] _bulletsRigidbody;
    private Rigidbody _viewRidigbody;

    private List<Ray> _enemyHits;
    private Dictionary<float, Ray> _rayDistanceDictionary;

    private GameObject _bulletsPool;
    private GameObject _bulletPrefab;

    private Vector3 _bulletStartPosition;

    public ShootingController(GameObjectView view)
    {
        _bulletsRigidbody = new Rigidbody[Parameters.BULLET_POOL_CAPACITY];

        _bulletController = new BulletsController(_bulletsRigidbody);
        _enemyHits = new List<Ray>();
        _rayDistanceDictionary = new Dictionary<float, Ray>();
        

        View = view;
        _viewRidigbody = View.GetComponent<Rigidbody>();

        _bulletStartPosition = View.transform.Find(Parameters.BARREL_OBJECT_NAME).transform.position;

        CreateBulletsContainer();
        CreateBullets(Parameters.BULLET_POOL_CAPACITY);
    }

    /// <summary>
    /// Creates gameObject that contains all bullets on the scene.
    /// </summary>
    private void CreateBulletsContainer()
    {
        _bulletPrefab = View.BulletPrefab;
        _bulletsPool = new GameObject(Parameters.BULLET_POOL_OBJECT_NAME);
    }

    /// <summary>
    /// Creates given amount of bullets on the scene and adds them to BulletsContainer.
    /// </summary>
    /// <param name="amount">Amount of bullets to create.</param>
    private void CreateBullets(int amount)
    {
        for (int i = 0; i < amount; i++)
        {
            var tempBullet = GameObject.Instantiate(_bulletPrefab, _bulletsPool.transform);
            var bulletPosition = Vector3.zero;
            var bulletRigidbody = tempBullet.GetComponent<Rigidbody>();
            _bulletsRigidbody[i] = bulletRigidbody;

            tempBullet.transform.position = bulletPosition;
            bulletRigidbody.angularDrag = Parameters.BULLET_ANGULAR_DRAG;
            bulletRigidbody.drag = Parameters.BULLET_DRAG;
            bulletRigidbody.gameObject.name = $"Bullet {i}";

            tempBullet.SetActive(false);
        }
    }

    public void Update()
    {
        _bulletStartPosition = View.transform.Find(Parameters.BARREL_OBJECT_NAME).transform.position;

        // Ony try to shoot if standing still and there is no cooldown.
        if (_viewRidigbody.velocity == Vector3.zero && _shootInterval < 0f)
        {
            if (SearchEnemy(Parameters.PLAYER_FOV, Parameters.FOV_RAYCAST_STEP, Parameters.FOV_RAYCAST_MAXDISTANCE))
            {
                // Find shortest ray that hit enemy.
                float[] keys = _rayDistanceDictionary.Keys.ToArray();
                Ray shortestRay;
                _rayDistanceDictionary.TryGetValue(keys.Min(), out shortestRay);

                Shoot(shortestRay);

                // Reset shooting cooldown and delete all saved rays for the next frame.
                _shootInterval = 1 / Parameters.SHOOTING_RATE;
                _rayDistanceDictionary.Clear();
                return;
            }
        }
        // If shooting is on cooldown, reduce it by deltaTime;
        if (_shootInterval > 0f) 
        {
            _shootInterval -= Time.deltaTime;
        }
    }

    public void FixedUpdate()
    {
       _bulletController.FixedUpdate();
    }

    /// <summary>
    /// Returns if enemy is inside cone-shaped area with given parameters.
    /// </summary>
    /// <param name="angleInDeg">Cone diameter in degrees.</param>
    /// <param name="stepSizeInDeg">Interval between rays cast.</param>
    /// <param name="maxRayDistance">Maximum ray magnitude.</param>
    /// <returns></returns>
    private bool SearchEnemy(int angleInDeg, float stepSizeInDeg, int maxRayDistance)
    {
        Vector3 origin = _bulletStartPosition;
        RaycastHit hit;
        Vector3 forwardDir;

        _enemyHits.Clear();

        for (int i = -angleInDeg / 2; i <= angleInDeg / 2; i += (int)stepSizeInDeg)
        {
            forwardDir = View.transform.forward;

            Quaternion rayRotation = Quaternion.Euler(0f, i, 0f);
            Vector3 rayDirection = rayRotation * forwardDir;

            ////////
            //Debug.DrawRay(origin, rayDirection.normalized * 10, Color.cyan);
            ////////

            // If raycast hits something, check if it is enemy, if so add that ray and its distance to dictionary.
            if (Physics.Raycast(origin, rayDirection.normalized, out hit, maxRayDistance))
            {
                if (hit.collider.gameObject.CompareTag(Parameters.ENEMY_TAG))
                {
                    if(_rayDistanceDictionary.ContainsKey(hit.distance))
                    {
                        _rayDistanceDictionary.Remove(hit.distance);
                    }
                    _rayDistanceDictionary.Add(hit.distance, new Ray(origin, hit.collider.ClosestPoint(origin)-origin));
                }
            }
        }
        return _rayDistanceDictionary.Count > 0;
    }

    /// <summary>
    /// Requests next availible bullet, resets its parameters and shoots in given direction.
    /// </summary>
    /// <param name="shootRay">Ray that describes shots origin and diretion.</param>
    private void Shoot(Ray shootRay)
    {
        ////////
        Debug.DrawRay(shootRay.origin, shootRay.direction.normalized * Parameters.FOV_RAYCAST_MAXDISTANCE, Color.red);
        ////////
        
        Rigidbody bullet = GetNextBullet();
        if (bullet != null)
        {
            ResetBullet(bullet);
            bullet.AddForce(shootRay.direction.normalized * Parameters.BULLET_SHOOT_VELOCITY, ForceMode.Impulse);
            return;
        }
        Debug.LogException(new NullReferenceException("Couldn't find availible bullet. GetNextBullet returned null."));
    }

    /// <summary>
    /// Sets given bullet's position, rotation and velocity to default values.
    /// </summary>
    /// <param name="bullet">Bullet's rigidBody.</param>
    private void ResetBullet(Rigidbody bullet)
    {
        bullet.transform.position = _bulletStartPosition;
        bullet.velocity = Vector3.zero;
        bullet.rotation = Quaternion.identity;
    }

    /// <summary>
    /// Checks next availiable bullet in the pool and enables it. If it's last availiable bullet, disables next in the array.
    /// When end of array is reached, jumps back to 0 index.
    /// </summary>
    /// <returns>Returns rigidBody of enabled bullet.</returns>
    private Rigidbody GetNextBullet()
    {
        // Find inactive bullet and use it.
        Rigidbody bullet = FindInactiveBullet(_bulletsRigidbody);

        // If there is no inactive bullets, find bullet that is active but not moving and reuse that bullet.
        bullet = bullet == null ? FindSleepingBullet(_bulletsRigidbody) : bullet;

        // If it is active and moving but we ran out of availiable bullet, force this
        // bullet to be reset and reused.
        bullet = bullet == null ? FindFarthestBullet(_bulletsRigidbody) : bullet;

        if (bullet == null)
        {
            Debug.LogException(new Exception("Cannot find availiable bullet."));
        }

        bullet.gameObject.SetActive(true);
        return bullet;
    }

    private Rigidbody FindInactiveBullet(Rigidbody[] bullets)
    {
        return bullets.FirstOrDefault(b => !b.gameObject.activeSelf);
    }    
    private Rigidbody FindSleepingBullet(Rigidbody[] bullets)
    {
        return bullets.FirstOrDefault(b => b.gameObject.activeSelf && b.velocity.magnitude < Parameters.BULLET_VELOCITY_THRESHOLD);
    }    
    private Rigidbody FindFarthestBullet(Rigidbody[] bullets)
    {
        float maxDist = bullets.Max(p => p.gameObject.transform.position.magnitude);
        Rigidbody farthestBullet = bullets.First(p => p.gameObject.transform.position.magnitude == maxDist);

        return farthestBullet;
    }
}
