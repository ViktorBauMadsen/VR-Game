using System.Collections.Generic;
using UnityEngine;

// The little drinks menu that pops up over the bar when you press the
// DRINKS button: one card per drink, each with a slowly turning model of the
// drink and its name and price. Point at a card (or touch it) and press grab
// to order it (DrinkMenuItem). It folds away again after ordering, when you
// press DRINKS again, when you walk away, or after a while.
public class DrinkMenu : MonoBehaviour
{
    [Tooltip("Shown while the menu is open (all the cards are under it).")]
    [SerializeField] Transform panel;
    [SerializeField] float closeDistance = 3f;
    [SerializeField] float autoCloseSeconds = 30f;
    [SerializeField] float previewSpin = 35f;

    readonly List<DrinkMenuItem> _items = new List<DrinkMenuItem>();
    Transform _player;
    float _openedAt, _anim;
    Vector3 _panelScale = Vector3.one;

    public bool IsOpen { get; private set; }

    void Awake()
    {
        if (panel == null) panel = transform;
        _panelScale = panel.localScale;
        GetComponentsInChildren(true, _items);
        panel.gameObject.SetActive(false);
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        IsOpen = true;
        _openedAt = Time.time;
        _anim = 0f;
        panel.localScale = _panelScale * 0.01f;
        panel.gameObject.SetActive(true);
        BarSounds.Play(BarSounds.Ding, panel.position, 0.25f);
    }

    public void Close()
    {
        IsOpen = false;
    }

    void Update()
    {
        if (_player == null && Camera.main != null) _player = Camera.main.transform;

        if (IsOpen)
        {
            bool tooFar = _player != null && Vector3.Distance(_player.position, panel.position) > closeDistance;
            if (tooFar || Time.time - _openedAt > autoCloseSeconds) Close();
        }

        // Pops open / folds away.
        if (!panel.gameObject.activeSelf) return;
        _anim = Mathf.MoveTowards(_anim, IsOpen ? 1f : 0f, Time.deltaTime / 0.18f);
        float s = IsOpen ? 1f - (1f - _anim) * (1f - _anim) : _anim * _anim;
        panel.localScale = _panelScale * Mathf.Max(0.01f, s);
        if (!IsOpen && _anim <= 0f)
        {
            panel.gameObject.SetActive(false);
            return;
        }

        foreach (var item in _items)
            if (item != null && item.Preview != null)
                item.Preview.Rotate(0f, previewSpin * Time.deltaTime, 0f, Space.Self);
    }
}
