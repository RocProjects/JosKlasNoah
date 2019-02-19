﻿using Custom.GameManager;
using System.Collections.Generic;
using UnityEngine;
public interface IInteractable
{
    void OnItemInteract(PlayerController owningPlayer);
    void OnItemRightMouseButton(PlayerController owningPlayer);
    void UpdateObjectOffset(float newPosistion);
}

//this object needs a rigidbody and capsule collider to make sure it has it We make unity require it
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class PlayerController : MonoBehaviour
{
    #region vars
    //lenght of the raycast from player feet
    const float groundDistanceAllowed = .05f;

    [Header("Movement")]
    [SerializeField]
    float _moveSpeed = 2;
    [SerializeField, Range(0, 1)]
    float _sprintSpeed = .2f;
    [SerializeField, Range(0, 1)]
    float _crouchSpeed = .8f;
    [SerializeField, Range(0, 1)]
    float _airSpeed = .1f;

    [SerializeField, Header("Jumping")]
    float _jumpHeight = 5;
    [SerializeField]
    int _maxJumpCount = 1;
    [SerializeField]
    float _jumpDelay = 0f;

    [SerializeField, Header("Height")]
    float _normalHeight = 2.2f;
    [SerializeField]
    float _crouchHeight = 1.5f;

    [SerializeField, Header("ObjectInteraction")]
    float _objectInteractionDistance = 2.2f;
    public IInteractable holdingObject = null;
    IInteractable hitObjectInterface = null; // object we are currently overlapping with

    [SerializeField, Header("Debug")]
    bool _debugRays = false;


    Vector3 _moveInput = new Vector3();
    bool _isOnGround;
    Vector3 _currentGroundVelocity;
    List<RaycastHit> _latestRayHits = new List<RaycastHit>();


    //component references
    [SerializeField, HideInInspector]
    Camera _cam;
    [SerializeField, HideInInspector]
    Rigidbody _rb;
    [SerializeField, HideInInspector]
    CapsuleCollider _capsuleCollider;

    //MousePosistionData
    Vector2 _mouseInput;

    //JumpData
    bool _jumpKeyPressed = false;
    int _currentJumpCount = 0;
    float _currentJumpDelay = 0;

    bool _shouldStopCrouching = true;
    #endregion

    public int JumpCount { get { return _maxJumpCount; } set { _maxJumpCount = value; } }
    public Camera Cam { get { return _cam; } set { _cam = value; } }

    #region Editor
    private void Reset()
    {
        //set default rigidbody settings
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.mass = 1;

        //set capsule settings
        _capsuleCollider = GetComponent<CapsuleCollider>();
        _capsuleCollider.material.staticFriction = 0;
        _capsuleCollider.material.dynamicFriction = 0;
        _capsuleCollider.material.frictionCombine = PhysicMaterialCombine.Minimum;

        // get camera
        _cam = GetComponentInChildren<Camera>();
        //if the object has no child with a camera
        if (_cam == null)
        {
            //create new gameobject set his parrent and his localposistion
            GameObject camObj = new GameObject("MainCamera");
            camObj.transform.SetParent(transform);

            //add the components
            _cam = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
        }
        ChangeHeight(_normalHeight);

        _cam.gameObject.tag = "MainCamera";
        _cam.backgroundColor = Color.black;
    }
    #endregion

    private void Awake()
    {
        Application.targetFrameRate = -1;

        if (_capsuleCollider == null)
        {
            _capsuleCollider = GetComponent<CapsuleCollider>();
        }

        if (_cam == null)
        {
            _cam = GetComponentInChildren<Camera>();
        }

        if (_rb == null)
        {
            _rb = GetComponent<Rigidbody>();
        }

        Cursor.lockState = CursorLockMode.Locked;
        GameManager.CurrentPlayerController = this;
    }

    private void Update()
    {
        #region Movement

        if (Input.GetButtonDown("Jump"))
        {
            _jumpKeyPressed = true;
        }

        _moveInput = transform.right * Input.GetAxis("Horizontal") + transform.forward * Input.GetAxis("Vertical");
        #endregion

        #region LookAround

        //float mouseLookRotation will be set equal to the x value of the mousemovement multiplied by the sensitivity divined in the gameManager
        _mouseInput.x += Input.GetAxis("Mouse X") * GameManager.MouseVelocity * Time.fixedDeltaTime;
        //float mouseLookUp will be set equal to its old value + the current Y axis of the mouse multiplied by the sensitivity. the result wil be between -80 and 80
        _mouseInput.y = Mathf.Clamp(_mouseInput.y - (Input.GetAxis("Mouse Y") * GameManager.MouseVelocity * Time.fixedDeltaTime), -80, 80);

        #endregion

        if (Input.GetButtonDown("Crouch"))
        {
            ChangeHeight(_crouchHeight);
            _shouldStopCrouching = false;
        }

        if (IsCrouching())
        {
            if (Input.GetButtonUp("Crouch"))
            {
                if (CanStopCrouching())
                {
                    ChangeHeight(_normalHeight);
                }

                _shouldStopCrouching = true;
            }
            else if (_shouldStopCrouching)
            {
                if (CanStopCrouching())
                {
                    ChangeHeight(_normalHeight);
                }
            }
        }

        OnObjectInteraction();

    }

    private void FixedUpdate()
    {
        _isOnGround = IsGrounded();
        _currentGroundVelocity = GetGroundMovingSpeed();

        #region Movement
        if (_jumpKeyPressed)
        {
            _jumpKeyPressed = false;
            Jump();
            Debug.Log("jump key detected");
        }

        _moveInput = Vector3.Normalize(_moveInput) * MinMaxMoveSpeed();

        float currentMinMax = MinMaxMoveSpeed() * Time.fixedDeltaTime;

        _moveInput = new Vector3(
            Mathf.Clamp(_moveInput.x * Time.fixedDeltaTime, -currentMinMax, currentMinMax), //x
           !_isOnGround || !_jumpKeyPressed ? _rb.velocity.y : -Physics.gravity.y * Time.fixedDeltaTime, //y
            Mathf.Clamp(_moveInput.z * Time.fixedDeltaTime, -currentMinMax, currentMinMax) //z
            );

        _rb.velocity = _moveInput + _currentGroundVelocity + (Physics.gravity * Time.fixedDeltaTime);

        #endregion

        _rb.transform.rotation = Quaternion.Euler(0, _mouseInput.x, 0);
        _cam.transform.localRotation = Quaternion.Euler(_mouseInput.y, 0, 0);
    }

    #region Movement
    void Jump()
    {
        if (_debugRays)
        {
            DrawJumpRay();
        }

        //als we op de grond zijn
        if (IsGrounded())
        {
            _currentJumpCount = 0;
        }
        //als we niet op de grond zijn , als we niet nog een keer mogen springen
        else if (_currentJumpCount >= _maxJumpCount || Time.time < _jumpDelay)
        {
            return;
        }

        if (_rb.velocity.y < 0)
        {
            _rb.velocity += Vector3.down * _rb.velocity.y;
            Debug.Log(_rb.velocity);
        }

        _rb.AddForce(Vector3.up * _jumpHeight, ForceMode.Impulse);
        _currentJumpDelay = Time.time + _jumpDelay;
        _currentJumpCount++;
    }

    Vector3 GetGroundMovingSpeed()
    {
        for (int i = 0; i < _latestRayHits.Count; i++)
        {
            if (_latestRayHits[i].rigidbody == null)
            {
                continue;
            }

            return new Vector3(_latestRayHits[i].rigidbody.velocity.x, 0, _latestRayHits[i].rigidbody.velocity.z);
        }

        return Vector3.zero;

    }

    void DrawJumpRay()
    {
        Bounds CapsuleBounds = GetComponent<CapsuleCollider>().bounds;
        Vector3 rayStartPos = transform.position - Vector3.up * (CapsuleBounds.extents.y * .80f);
        Debug.DrawRay(rayStartPos, Vector3.down * ((CapsuleBounds.extents.y * .20f) + groundDistanceAllowed), Color.red, 10);

        Debug.DrawRay(rayStartPos + (transform.forward * CapsuleBounds.extents.z), Vector3.down * ((CapsuleBounds.extents.y * .20f) + groundDistanceAllowed), Color.red, 10);
        Debug.DrawRay(rayStartPos + (transform.forward * -CapsuleBounds.extents.z), Vector3.down * ((CapsuleBounds.extents.y * .20f) + groundDistanceAllowed), Color.red, 10);
        Debug.DrawRay(rayStartPos + (transform.right * CapsuleBounds.extents.x), Vector3.down * ((CapsuleBounds.extents.y * .20f) + groundDistanceAllowed), Color.red, 10);
        Debug.DrawRay(rayStartPos + (transform.right * -CapsuleBounds.extents.x), Vector3.down * ((CapsuleBounds.extents.y * .20f) + groundDistanceAllowed), Color.red, 10);
    }

    bool IsGrounded()
    {
        if (_debugRays)
        {
            DrawJumpRay();
        }

        Bounds CapsuleBounds = _capsuleCollider.bounds;
        Vector3 rayStartPos = transform.position - Vector3.up * (CapsuleBounds.extents.y * .80f);

        RaycastHit Ray1 = new RaycastHit();
        RaycastHit Ray2 = new RaycastHit();
        RaycastHit Ray3 = new RaycastHit();
        RaycastHit Ray4 = new RaycastHit();
        RaycastHit Ray5 = new RaycastHit();


        bool temp = Physics.Raycast(rayStartPos, Vector3.down, out Ray1, (CapsuleBounds.extents.y * .20f) + groundDistanceAllowed)
            || Physics.Raycast(rayStartPos + (transform.forward * CapsuleBounds.extents.z), Vector3.down, out Ray2, (CapsuleBounds.extents.y * .20f) + groundDistanceAllowed)
            || Physics.Raycast(rayStartPos + (transform.forward * -CapsuleBounds.extents.z), Vector3.down, out Ray3, (CapsuleBounds.extents.y * .20f) + groundDistanceAllowed)
            || Physics.Raycast(rayStartPos + (transform.right * CapsuleBounds.extents.x) + (transform.forward * -CapsuleBounds.extents.z), Vector3.down, out Ray4, (CapsuleBounds.extents.y * .20f) + groundDistanceAllowed)
            || Physics.Raycast(rayStartPos + (transform.right * -CapsuleBounds.extents.x), Vector3.down, out Ray5, (CapsuleBounds.extents.y * .20f) + groundDistanceAllowed);

        _latestRayHits = new List<RaycastHit>() { Ray1, Ray2, Ray3, Ray4, Ray5 };

        return temp;
    }

    bool CanStopCrouching()
    {
        return !Physics.Raycast(transform.position + Vector3.up * (_capsuleCollider.bounds.extents.y * .95f), Vector3.up, _normalHeight - _crouchHeight);
    }

    float MinMaxMoveSpeed()
    {
        float _tempAddSpeed = 1;

        if (IsCrouching())
        {
            _tempAddSpeed = _crouchSpeed * 100;
        }
        else if (Input.GetButton("Sprint"))
        {
            _tempAddSpeed = 100 * (_sprintSpeed + 1);
        }


        return _moveSpeed * (IsCrouching() ? 100 * _crouchSpeed : 100 * (Input.GetButton("Sprint") ? 1 + _sprintSpeed : 1));
    }

    void ChangeHeight(float newHeight)
    {
        float difference = (_capsuleCollider.height - newHeight) * .5f * 2;
        _capsuleCollider.height = newHeight;
        _cam.transform.localPosition = Vector3.up * _capsuleCollider.height * .41f;
        _rb.AddForce(Vector3.down * difference, ForceMode.VelocityChange);
    }

    bool IsCrouching()
    {
        return _capsuleCollider.height == _crouchHeight;
    }

    #endregion
    void OnObjectInteraction()
    {
        RayCheck();

        if (Input.GetMouseButtonDown(0))
        {
            if (holdingObject != null)
            {
                holdingObject.OnItemInteract(this);
            }
            else
            {
                if (hitObjectInterface != null)
                {
                    hitObjectInterface.OnItemInteract(this);
                }

            }

        }
        else if (Input.GetMouseButtonDown(1))
        {
            if (holdingObject != null)
            {
                holdingObject.OnItemRightMouseButton(this);
            }
        }
    }

    void RayCheck()
    {
        if (_debugRays)
        {
            //visible line in editor
            Debug.DrawRay(_cam.gameObject.transform.position, _cam.gameObject.transform.forward * 2f, Color.green, 5);
        }

        //create a new raycasthit
        RaycastHit hit = new RaycastHit();

        //shoot a ray forward , and only check the layers specified in the editor
        //IF we hit something
        if (Physics.Raycast(_cam.transform.position, _cam.transform.forward, out hit, _objectInteractionDistance))
        {
            //If the hit object has a rigidbody
            if (hit.rigidbody != null)
            {
                //hitobject is equal to the IInteractable from the hit object (if it has any),other wise its null 
                hitObjectInterface = hit.rigidbody.gameObject.GetComponent<IInteractable>();
            }
            else if (holdingObject != null)
            {
                holdingObject.UpdateObjectOffset(hit.distance);
            }
            else
            {
                hitObjectInterface = null;
            }
        }
        //IF we did not hit something with the ray
        else
        {
            //clear the hitobject
            hitObjectInterface = null;
            if (holdingObject != null)
            {
                holdingObject.UpdateObjectOffset(1500);
            }
        }
    }

}