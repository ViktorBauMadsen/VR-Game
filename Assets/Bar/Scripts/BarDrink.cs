using UnityEngine;

// Hold the glass up to your mouth to drink: the liquid drains while it's
// there, with a gulp every so often. Finishing the glass kicks in
// DrunkEffect for a few seconds. The empty glass disappears a little while
// after it's put down.
public class BarDrink : BarConsumable
{
    [Tooltip("Scaled on Y from 1 (full) to 0 (empty); the liquid mesh sits above this pivot.")]
    [SerializeField] Transform liquidPivot;
    [SerializeField] float drinkSeconds = 1.8f;
    [SerializeField] float drunkSeconds = 8f;

    float _fill = 1f;
    float _gulpTimer;

    protected override void Update()
    {
        base.Update();
        if (_fill <= 0f || !IsHeld || !AtMouth) return;

        _gulpTimer -= Time.deltaTime;
        if (_gulpTimer <= 0f)
        {
            _gulpTimer = 0.45f;
            BarSounds.Play(BarSounds.Gulp, MouthPosition, 0.5f);
            Haptic(0.3f, 0.08f);
        }

        _fill = Mathf.Max(0f, _fill - Time.deltaTime / drinkSeconds);
        if (liquidPivot != null)
        {
            liquidPivot.localScale = new Vector3(1f, Mathf.Max(_fill, 0.001f), 1f);
            if (_fill <= 0f) liquidPivot.gameObject.SetActive(false);
        }

        if (_fill <= 0f)
        {
            DrunkEffect.AddDrink(drunkSeconds);
            MarkSpent();
        }
    }
}
