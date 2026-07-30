using System.Collections;
using UnityEngine;

/// <summary>
/// [시스템/뷰 연출] 전투 발생 시 데미지 수치를 가독성 있게 시각화하는 클래스입니다.
/// 3매치 퍼즐의 특성상 동시다발적인 데미지가 발생하므로, 각 팝업은 독립적인 생명주기와 연출 로직을 가집니다.
/// 숫자 폰트 대신 개별 스프라이트 기반 자릿수 시스템을 사용하여 스타일리시한 UI 연출이 가능합니다.
/// </summary>
public class DamagePopup : MonoBehaviour
{
    [Header("Digits (비주얼 데이터 바인딩)")]
    [Tooltip("숫자의 각 자릿수를 표현할 스프라이트 렌더러 배열입니다. (최대 자릿수 제한 역할)")]
    [SerializeField] private SpriteRenderer[] digitRenderers;
    [Tooltip("0~9 인덱스에 대응하는 숫자 이미지 리스트입니다. (Asset 품질이 곧 연출 품질)")]
    [SerializeField] private Sprite[] digitSprites;
    [Tooltip("개별 숫자의 크기 배율입니다. 1.0f 미만 사용 시 여유 공간을 확보할 수 있습니다.")]
    [SerializeField] private float digitScaleMultiplier = 0.85f;

    [Header("Render Layer (렌더링 무결성)")]
    [Tooltip("활성화 시 씬 내 다른 스프라이트(몬스터, 블록)보다 항상 위에 표시되도록 소팅을 강제합니다.")]
    [SerializeField] private bool overrideSorting = true;
    [Tooltip("UI 레이어나 전용 데미지 연출 레이어 이름을 기입하십시오.")]
    [SerializeField] private string sortingLayerName = "Default";
    [Tooltip("동일 레이어 내에서 데미지 숫자가 몬스터 등보다 앞에 오도록 높은 값을 권장합니다.")]
    [SerializeField] private int orderInLayer = 500;

    [Header("Motion & Timing (물리 및 시간 연출)")]
    [Tooltip("자릿수 사이의 픽셀/단위 거리입니다. 값이 크면 숫자가 넓게 퍼져 보입니다.")]
    [SerializeField] private float digitSpacing = 0.8f;
    [Tooltip("팝업이 생성된 후 위(Y+) 방향으로 이동할 총 거리입니다. (부동 효과)")]
    [SerializeField] private float moveY = 0.8f;
    [Tooltip("화면에 머무는 시간입니다. 지나치게 길면 화면이 복잡해지며, 짧으면 가독성이 떨어집니다.")]
    [SerializeField] private float lifetime = 1.2f;
    
    // [최적화 데이터] 연출 도중 실시간 계산을 줄이기 위해 초기 스케일 값을 캐싱하여 메모리에 보관합니다.
    private Vector3[] baseDigitScales;

    private void Awake()
    {
        // 1. 초기 참조 바인딩: 수동 할당 누락 시 런타임에서 지연 시간이 발생하지 않도록 미리 확보합니다.
        AutoBindDigitRenderersIfNeeded();
        // 2. 초기 기하 정보 보존: 연출에 따라 스케일이 변형될 수 있으므로 원본 데이터를 백업합니다.
        CacheBaseDigitScales();
    }

    /// <summary>
    /// [방어적 프로그래밍] 인스펙터 직렬화 데이터가 손상되었거나 누락된 경우, 자식 계층을 탐색하여 복구합니다.
    /// </summary>
    private void AutoBindDigitRenderersIfNeeded()
    {
        // 이미 할당된 경우 탐색 비용을 절약하기 위해 즉시 반환(Early Return)
        if (digitRenderers != null && digitRenderers.Length > 0)
            return;

        // 비활성 상태의 자식까지 포함하여 렌더러 탐색
        var srs = GetComponentsInChildren<SpriteRenderer>(true);
        if (srs == null || srs.Length == 0)
            return;

        // 리스트를 사용하여 런타임에 동적으로 유효성 검사 수행
        var list = new System.Collections.Generic.List<SpriteRenderer>(srs.Length);
        for (int i = 0; i < srs.Length; i++)
        {
            // 부모(자신)의 렌더러가 아닌, 순수 숫자를 위한 자식 오브젝트만 필터링 
            if (srs[i] != null && srs[i].transform != transform)
                list.Add(srs[i]);
        }

        digitRenderers = list.ToArray();
    }

    /// <summary>
    /// 원본 스케일 데이터를 캐싱하여, 연출 과정에서 숫자의 배율이 안정적으로 유지되도록 합니다.
    /// </summary>
    private void CacheBaseDigitScales()
    {
        if (digitRenderers == null)
        {
            baseDigitScales = null;
            return;
        }

        // 캐싱 배열의 크기가 현재 렌더러 목록과 일치하는지 확인
        if (baseDigitScales != null && baseDigitScales.Length == digitRenderers.Length)
            return;

        baseDigitScales = new Vector3[digitRenderers.Length];
        for (int i = 0; i < digitRenderers.Length; i++)
        {
            if (digitRenderers[i] != null)
                baseDigitScales[i] = digitRenderers[i].transform.localScale;
            else
                baseDigitScales[i] = Vector3.one;
        }
    }

    /// <summary>
    /// 데미지 정수값을 시각적 레이아웃으로 변환하여 연출을 활성화합니다.
    /// 핵심 로직: 정수 -> 문자열 -> 자릿수 분할 -> 좌표 계산 -> 레이아웃 배치
    /// </summary>
    /// <param name="value">적용할 데미지 수치 (절대값 처리)</param>
    /// <param name="customColor">적용할 색상 (기본값 하얀색)</param>
    public void Setup(int value, Color? customColor = null, bool isCritical = false)
    {
        Color targetColor = customColor ?? Color.white;
        float finalScaleMultiplier = digitScaleMultiplier;

        // [v14.37] 크리티컬인 경우 색상을 금색으로 변경
        if (isCritical)
        {
            targetColor = new Color(1f, 0.8f, 0f); // Gold
            moveY *= 1.5f; // 더 높이 솟구침
            lifetime *= 1.2f; // 조금 더 오래 유지
        }
        // 런타임 안정성 재확보
        AutoBindDigitRenderersIfNeeded();
        CacheBaseDigitScales();

        // [예외 처리] 필수 데이터 누락 시 팝업을 즉시 제거하여 씬 노이즈를 방지합니다.
        if (digitRenderers == null || digitRenderers.Length == 0 || digitSprites == null || digitSprites.Length < 10)
        {
            Debug.LogWarning($"[DamagePopup] 구성 데이터 부족 - Renderers:{digitRenderers?.Length}, Sprites:{digitSprites?.Length}");
            Destroy(gameObject);
            return;
        }

        // 음수 데미지(회복 등) 표기를 위해 절대값으로 변환하여 문자열 시퀀스 생성
        string textValue = Mathf.Abs(value).ToString();

        // 1. 모든 렌더러 초기화 및 소팅 강제 설정
        for (int i = 0; i < digitRenderers.Length; i++)
        {
            if (digitRenderers[i] == null) continue;

            if (overrideSorting)
            {
                digitRenderers[i].sortingLayerName = sortingLayerName;
                digitRenderers[i].sortingOrder = orderInLayer;
            }
            digitRenderers[i].gameObject.SetActive(false);
        }

        // 2. 출력 가능한 자릿수 한계치 계산
        int length = Mathf.Min(textValue.Length, digitRenderers.Length);
        if (length <= 0)
        {
            Destroy(gameObject);
            return;
        }

        // 3. 수평 중앙 정렬 계산 (Center-Alignment Math)
        // 전체 너비 = (실제 자릿수 - 1) * 간격
        // 시작 지점 = 전체 너비의 절반만큼 왼쪽(-)으로 이동
        float totalWidth = (length - 1) * digitSpacing;
        float startX = -totalWidth * 0.5f;

        // 4. 숫자에 따른 스프라이트 매핑 및 좌표 배치
        for (int i = 0; i < length; i++)
        {
            // ASCII 차를 이용한 빠른 문자-정수 변환
            int digit = textValue[i] - '0';
            SpriteRenderer sr = digitRenderers[i];
            if (sr == null) continue;

            sr.gameObject.SetActive(true);
            // 해당 자릿수에 맞는 스프라이트 데이터 로드
            sr.sprite = digitSprites[digit];
            
            // X축은 중앙 정렬 기준 i번째 간격, Y/Z는 0으로 고정
            sr.transform.localPosition = new Vector3(startX + i * digitSpacing, 0f, 0f);
            // 원본 스케일에 배율 적용하여 최종 크기 결정
            sr.transform.localScale = baseDigitScales[i] * finalScaleMultiplier;
            // 지정된 색상으로 초기화 (알파는 1.0)
            sr.color = targetColor;
        }

        // [v14.37] 크리티컬 시 화면 흔들림 효과 (카메라가 아닌 팝업 자체)
        if (isCritical) StartCoroutine(CriticalShakeRoutine());

        // 5. 비동기 연출 실행 (메인 스레드 부하 분산)
        StartCoroutine(PlayRoutine());
    }

    /// <summary>
    /// [코루틴] 데미지 숫자의 생명주기를 관리하는 물리/비주얼 애니메이션 루틴입니다.
    /// 위치 보간(Lerp)과 알파 페이딩(Alpha Fade)을 병행하여 자연스러운 소멸을 구현합니다.
    /// </summary>
    private IEnumerator PlayRoutine()
    {
        float elapsed = 0f;
        Vector3 origin = transform.position;
        // 최종 도달 위치 설정 (Y축 점진적 부상)
        Vector3 target = origin + new Vector3(0f, moveY, 0f);

        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            // 정규화된 시간 값 (0.0 ~ 1.0)
            float t = Mathf.Clamp01(elapsed / lifetime);

            // 1. 위치 이동: 시간에 따른 변위값 적용
            transform.position = Vector3.Lerp(origin, target, t);

            // 2. 비주얼 소멸: 뒤로 갈수록 가속되는 페이드 아웃 연출
            // t값이 1.0에 가까워질수록 alpha는 0.0에 수렴
            float alpha = Mathf.Lerp(1f, 0f, t);
            for (int i = 0; i < digitRenderers.Length; i++)
            {
                // 불필요한 연산 방지를 위해 활성화된 요소만 색상 업데이트
                if (digitRenderers[i] == null || !digitRenderers[i].gameObject.activeSelf)
                    continue;

                Color c = digitRenderers[i].color;
                c.a = alpha;
                digitRenderers[i].color = c;
            }

            // 한 프레임 대기
            yield return null;
        }

        // 연출 완료 후 가비지 컬렉션 부하를 최소화하며 명시적 파괴
        Destroy(gameObject);
    }
    private IEnumerator CriticalShakeRoutine()
    {
        float shakeDuration = 0.4f;
        float shakeAmount = 0.15f;
        float elapsed = 0f;
        // PlayRoutine과 충돌하지 않도록 하기 위해 별도의 오프셋으로 관리하거나 
        // 그냥 transform.position에 더해줍니다 (PlayRoutine이 덮어쓰겠지만 떨림 효과는 보임)
        
        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;
            transform.position += (Vector3)UnityEngine.Random.insideUnitCircle * shakeAmount;
            yield return null;
        }
    }
}
