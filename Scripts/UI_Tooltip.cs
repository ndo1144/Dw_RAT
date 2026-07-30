using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_Tooltip : MonoBehaviour
{
    public static UI_Tooltip Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI itemDescriptionText;
    [SerializeField] private RectTransform rectTransform;

    private CanvasGroup canvasGroup;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        InitializeIfNeeded();
        
        // [v14.48] SetActive(false) 대신 Alpha를 0으로 설정하여 초기화 지연 방지
        Hide();
    }

    private void InitializeIfNeeded()
    {
        if (Instance == null) Instance = this;
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    public void Show(string name, string description)
    {
        InitializeIfNeeded();

        // [v14.49] 오브젝트가 꺼져 있다면 먼저 활성화
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        itemNameText.text = name;
        itemDescriptionText.text = description;

        itemNameText.raycastTarget = false;
        itemDescriptionText.raycastTarget = false;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f; 
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        UpdatePosition();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        
        transform.SetAsLastSibling(); 
    }

    public void Hide() 
    {
        InitializeIfNeeded();
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    private void Update()
    {
        if (canvasGroup != null && canvasGroup.alpha > 0.1f)
        {
            UpdatePosition();
        }
    }

    private void UpdatePosition()
    {
        Vector2 mousePos = Input.mousePosition;

        float pivotX = mousePos.x > Screen.width * 0.5f ? 1f : 0f;
        float pivotY = mousePos.y > Screen.height * 0.5f ? 1f : 0f;

        rectTransform.pivot = new Vector2(pivotX, pivotY);

        float offsetX = (pivotX == 0) ? 20f : -20f;
        float offsetY = (pivotY == 0) ? 20f : -20f;

        rectTransform.position = mousePos + new Vector2(offsetX, offsetY);
    }
}