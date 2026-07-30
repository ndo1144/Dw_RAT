using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossRat : MonoBehaviour
{
    [Header("Animation")]
    [SerializeField] private float runAnimSpeed = 0.08f; // 달릴 때 속도
    [SerializeField] private float idleAnimSpeed = 0.15f; // 부착 후 속도
    [SerializeField] private float jumpDuration = 0.8f;   // 점프 소요 시간

    [Header("Sprites")]
    public Sprite[] runSprites; // 제공된 6개의 이미지 (0~5)
    public Sprite attachSprite; // 달라붙었을 때 고정 스프라이트

    private SpriteRenderer sr;
    private bool isAttached = false;
    private float animTimer = 0f;
    private int currentFrame = 0;

    private void Update()
    {
        // [v14.48] 부착된 상태(isAttached)에서는 애니메이션을 중단하고 고정된 상태를 유지함
        if (isAttached || runSprites == null || runSprites.Length == 0) return;

        animTimer += Time.deltaTime;
        float currentSpeed = runAnimSpeed;

        if (animTimer >= currentSpeed)
        {
            animTimer = 0f;
            
            // 이동 상태: 0~3번 프레임 반복 (달리는 연출)
            int maxRunFrame = Mathf.Min(3, runSprites.Length - 1);
            currentFrame = (currentFrame + 1) % (maxRunFrame + 1);

            if (sr != null) sr.sprite = runSprites[currentFrame];
        }
    }

    private void Awake()
    {
        // [v14.48] 보드판 중앙(0,0)에서 갑자기 튀어나오는 것 방지
        if (Vector3.Distance(transform.position, Vector3.zero) < 0.5f)
        {
            transform.position = new Vector3(100f, 100f, -5f);
        }

        // [v14.46] 컴포넌트가 없다면 강제로 추가하여 에러 방지
        sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>(true);
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();

        if (sr != null)
        {
            sr.enabled = true;
            sr.sortingLayerName = "Default"; 
            sr.sortingOrder = 2000; // 절대적으로 최상단
            
            // [v14.57] 생성 즉시 무작위 색상 부여 (황토색 밝기 상향)
            Color[] naturalRatColors = new Color[] {
                new Color(0.92f, 0.78f, 0.45f), // 밝은 황토색
                new Color(0.65f, 0.65f, 0.65f), // 회색
                new Color(0.4f, 0.25f, 0.15f),  // 진갈색
                Color.white                    // 흰색
            };
            sr.color = naturalRatColors[Random.Range(0, naturalRatColors.Length)];
            
            sr.transform.localPosition = Vector3.zero;
        }

        // [v14.58] 쥐 크기 재조정: 너무 크지 않게 0.85f로 설정 (원본 0.7f 대비 약 1.2배)
        transform.localScale = Vector3.one * 0.6f;

        // 애니메이터 간섭 차단
        Animator anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.enabled = false;

        if (runSprites == null || runSprites.Length == 0)
        {
            runSprites = Resources.LoadAll<Sprite>("Sprites/BossRat");
        }
    }

    private void Start()
    {
        // [중요] 시작 시점에 한 번 더 위치와 레이어 확인
        if (sr != null && runSprites != null && runSprites.Length > 0) 
        {
            sr.sprite = runSprites[0];
            sr.enabled = true;
            sr.sortingOrder = 2000;
        }
        
        // Z축 강제
        Vector3 p = transform.position;
        p.z = -5f;
        transform.position = p;
    }

    // [v14.46] 군집 연출용: 동시에 웅크리기
    public void PrepareJump()
    {
        if (sr != null && runSprites != null && runSprites.Length > 1) 
            sr.sprite = runSprites[1]; // 웅크린 프레임
    }

    // [v14.46] 군집 연출용: 실제 점프 이동
    public IEnumerator JumpToBlock(Block targetBlock)
    {
        Vector3 startPos = transform.position;
        startPos.z = -5f;
        transform.position = startPos;
        
        Vector3 targetWorldPos = targetBlock.transform.position;
        targetWorldPos.z = -5f;

        // 이미 웅크린 상태(PrepareJump)라고 가정하고 바로 뛰기 시작
        // [v14.46] 여기서 스프라이트를 고정하면 Update의 애니메이션과 충돌할 수 있으므로, 
        // Update 루틴이 처리하도록 둡니다.

        float duration = jumpDuration; 
        float elapsed = 0f;
        float startRotation = transform.localEulerAngles.z;
        float targetRotation = Random.Range(0, 8) * 45f; // 부착될 최종 각도 미리 결정

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            Vector3 currentPos = Vector3.Lerp(startPos, targetWorldPos, t);
            currentPos.z = -5f;
            transform.position = currentPos;
            
            // 점프 곡선 (살짝 위로 포물선)
            float yOffset = Mathf.Sin(Mathf.PI * t) * 1.2f;
            transform.position += new Vector3(0, yOffset, 0);

            // [v14.50] 비행 중 자연스러운 회전 (한 바퀴 돌며 타겟 각도로 안착)
            float currentRot = Mathf.Lerp(startRotation, targetRotation + 360f, t);
            transform.localRotation = Quaternion.Euler(0, 0, currentRot);

            elapsed += Time.deltaTime;
            yield return null;
        }

        // 도착 후 등반 및 부착
        transform.localRotation = Quaternion.Euler(0, 0, targetRotation); 
        if (sr != null && runSprites != null && runSprites.Length > 3) sr.sprite = runSprites[3];
        yield return new WaitForSeconds(0.15f);

        transform.SetParent(targetBlock.transform);
        isAttached = true;
        
        if (sr != null)
        {
            sr.enabled = true;
            sr.sortingOrder = 2000;
            if (runSprites != null && runSprites.Length > 4) sr.sprite = runSprites[4];
            else if (attachSprite != null) sr.sprite = attachSprite;
            
            // [v14.57] 색상은 이미 Awake에서 결정됨 (중복 설정 제거)
        }

        // [v14.58] 부착 시 Z값을 0으로 설정 (크기는 이미 Awake에서 설정됨)
        transform.localPosition = new Vector3(0, 0, 0f); 
        
        targetBlock.AssignStickyRat(this.gameObject);
        if (AudioDirector.Instance != null) AudioDirector.Instance.PlaySqueak();
    }

    public IEnumerator MoveToBlock(Vector3 startPos, Block targetBlock)
    {
        // 기존 MoveToBlock은 JumpToBlock으로 대체되거나 유지
        yield return StartCoroutine(JumpToBlock(targetBlock));
    }

    public void FallOff()
    {
        isAttached = false;
        transform.SetParent(null); // 부모(블록)와 분리
        
        // 물리 연출: 아래로 회전하며 추락
        StartCoroutine(FallRoutine());
    }

    private IEnumerator FallRoutine()
    {
        float gravity = 15f;
        Vector3 velocity = new Vector3(Random.Range(-2f, 2f), 5f, 0f); // 위로 살짝 튀어올랐다 추락
        float rotationSpeed = Random.Range(360f, 720f);

        float elapsed = 0f;
        while (elapsed < 2f)
        {
            velocity.y -= gravity * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);
            
            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(gameObject);
    }
}
