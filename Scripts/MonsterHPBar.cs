using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MonsterHPBar : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private Slider hpSlider;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI atkText; // [v14.46] 공격력 표시용 추가

    [Header("충전 UI (신규)")]
    [SerializeField] private GameObject chargeRoot;
    [SerializeField] private Slider chargeSlider;
    [SerializeField] private TextMeshProUGUI chargeText;

    [Header("설정")]
    [Tooltip("몬스터 오브젝트 기준 어느 정도 높이에 띄울지 설정 (예: 머리 위면 0.5)")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 0.5f, 0f);

    private CombatManager.MonsterInstance targetMonster;
    private Camera mainCamera;

    // [v14.75] 데미지 팝업용 위치 인식 API (Screen Space를 World Space로 변환하여 반환)
    public Vector3 GetTopWorldPosition()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        // 1. 박스 콜라이더 등 Collider2D가 있다면 그 상단 경계를 우선 사용
        Collider2D col = GetComponent<Collider2D>();
        if (col != null && mainCamera != null)
        {
            // 콜라이더의 현재 위치(Screen Space) 기준 가장 높은 Y값 계산
            Vector3 topScreenPos = new Vector3(transform.position.x, col.bounds.max.y, transform.position.z);
            
            // Screen Space -> World Space 변환 (Z값은 0으로 고정하여 2D 평면 유지)
            Vector3 worldPos = mainCamera.ScreenToWorldPoint(topScreenPos);
            worldPos.z = 0f;
            return worldPos;
        }

        // 2. 콜라이더가 없다면 몬스터 머리 위 기본 월드 좌표 사용
        if (targetMonster == null || targetMonster.obj == null) return transform.position;
        return targetMonster.obj.transform.position + offset;
    }

    // UIEffectManager_C 에서 생성할 때 최초 호출
    public void Initialize(CombatManager.MonsterInstance monster)
    {
        targetMonster = monster;
        mainCamera = Camera.main;

        // [v14.37] 충전 UI 자동 할당 및 초기화 (유연한 검색 로직 적용)
        if (chargeRoot == null) chargeRoot = transform.Find("ChargeRoot")?.gameObject;
        
        if (chargeRoot != null)
        {
            if (chargeSlider == null) chargeSlider = transform.Find("ChargeRoot/ChargeSlider")?.GetComponent<Slider>();
            if (chargeSlider == null) chargeSlider = chargeRoot.GetComponentInChildren<Slider>(); // 폴백: 하위 전체 검색

            if (chargeText == null) chargeText = transform.Find("ChargeRoot/ChargeText")?.GetComponent<TextMeshProUGUI>();
            if (chargeText == null) chargeText = chargeRoot.GetComponentInChildren<TextMeshProUGUI>(); // 폴백: 하위 전체 검색
        }

        // [v14.46] 공격력 텍스트 자동 할당
        if (atkText == null) atkText = transform.Find("AtkText")?.GetComponent<TextMeshProUGUI>();
        if (atkText == null) atkText = transform.Find("HPBar/AtkText")?.GetComponent<TextMeshProUGUI>();

        if (chargeRoot == null)
        {
            Debug.LogWarning($"[MonsterHPBar] {monster.name}의 ChargeRoot를 찾을 수 없습니다. (경로: {transform.name}/ChargeRoot)");
        }
        else if (chargeSlider == null || chargeText == null)
        {
            Debug.LogWarning($"[MonsterHPBar] {monster.name}의 ChargeRoot는 찾았으나 하위 컴포넌트가 누락되었습니다. (Slider: {chargeSlider != null}, Text: {chargeText != null})");
        }

        bool isEliteOrBoss = (monster.type == CombatManager.MonsterGrade.Elite || monster.type == CombatManager.MonsterGrade.Boss);
        if (chargeRoot != null) chargeRoot.SetActive(isEliteOrBoss);

        // [v14.45] 보스급인 경우 기본 오프셋을 높여서 캐릭터 스프라이트에 가려짐 방지
        if (monster != null && monster.type == CombatManager.MonsterGrade.Boss)
        {
            offset = new Vector3(-0.45f, 1.2f, 0f); // 보스 바로 위에 표시
        }
        
        // [헌법 §41] 생성 시 레이어 최상단으로 이동
        transform.SetAsLastSibling();

        UpdateHP(monster.hp, monster.maxHp);
        UpdateAtk(monster.atk); // [v14.46] 초기 공격력 표시
        
        if (isEliteOrBoss)
        {
            int current = (monster.type == CombatManager.MonsterGrade.Elite) ? monster.summonCooldownCounter : monster.bossChargeCount;
            int max = (monster.type == CombatManager.MonsterGrade.Elite) ? 3 : 2;
            UpdateCharge(current, max);
        }
    }

    // 체력 변화 시 호출
    public void UpdateHP(float currentHp, float maxHp)
    {
        if (hpSlider != null)
        {
            hpSlider.value = (maxHp > 0) ? currentHp / maxHp : 0;
        }

        if (hpText != null)
        {
            hpText.text = $"{Mathf.CeilToInt(currentHp)} / {Mathf.CeilToInt(maxHp)}";
        }
    }

    // [v14.37] 충전 상태 변화 시 호출
    public void UpdateCharge(int current, int max)
    {
        if (chargeRoot == null) return;
        
        if (!chargeRoot.activeSelf) chargeRoot.SetActive(true);

        if (chargeSlider != null)
        {
            chargeSlider.maxValue = max;
            chargeSlider.value = current;
        }
        else
        {
            Debug.LogWarning($"[MonsterHPBar] {targetMonster?.name}의 chargeSlider가 할당되지 않았습니다.");
        }

        if (chargeText != null)
        {
            chargeText.text = (current >= max) ? "READY!" : $"CHARGE: {current}/{max}";
        }
        else
        {
            Debug.LogWarning($"[MonsterHPBar] {targetMonster?.name}의 chargeText가 할당되지 않았습니다.");
        }
    }

    // [v14.46] 공격력 표시 갱신
    public void UpdateAtk(float atk)
    {
        if (atkText != null)
        {
            // 보스나 엘리트의 경우 특수 기믹이 있을 수 있으므로 표시 방식 고민 필요
            // 일단 기본 수치 표시
            atkText.text = $"ATK: {Mathf.CeilToInt(atk)}";
        }
    }

    // 매 프레임마다 몬스터의 위치를 추적하여 UI 캔버스 위로 위치 조정
    private void LateUpdate()
    {
        // 몬스터가 사라졌거나 파괴되었다면 체력바도 스스로 파괴
        if (targetMonster == null || targetMonster.obj == null)
        {
            Destroy(gameObject);
            return;
        }

        // 월드 좌표(3D/2D)를 캔버스 화면 좌표(Screen Point)로 변환해서 따라다니게 함
        if (mainCamera != null)
        {
            Vector3 worldPos = targetMonster.obj.transform.position + offset;
            Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPos);
            
            if (screenPos.z < 0)
            {
                transform.position = new Vector3(-2000, -2000, 0);
                return;
            }

            transform.position = screenPos;
        }

    }
}