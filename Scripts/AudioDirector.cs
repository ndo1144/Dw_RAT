using UnityEngine;

public class AudioDirector : MonoBehaviour
{
    public static AudioDirector Instance { get; private set; }

    [Header("Audio Sources")]
    public AudioSource bgmSource;      // 배경음 전용 (하나만 재생, 루프)
    public AudioSource sfxSource;      // 효과음 전용 (여러 개 중첩 재생)
    public AudioSource ambientSource;  // 환경음 전용 (배경음과 별도로 루프)

    [Header("BGM Settings")]
    public AudioClip peaceBGM;
    public AudioClip bossBGM;
    public AudioClip[] combatBGMs;

    [Header("Common SFX Settings")]
    public AudioClip buttonClickSFX;    // 버튼 클릭
    public AudioClip matchSFX;          // 매치 성공
    public AudioClip itemBuySFX;        // 아이템 구매
    public AudioClip itemUseSFX;        // 아이템 사용
    
    [Header("Character SFX")]
    public AudioClip mouseSqueakSFX;
    public AudioClip playerHitSFX;      // 플레이어 피격
    public AudioClip DeathSFX;   // 적 처치 시 (승리감)

    [Header("Special Action SFX")]
    public AudioClip bombReloadSFX;   // 폭탄 장전 (5매치 발생 시)
    public AudioClip bombThrowSFX;
    public AudioClip bombExplodeSFX;  // 폭탄 폭발 (클릭 시)
    public AudioClip strikeSFX;       // 퍼즐 폭행 (공격 마무리 시)
    private void Awake()
    {
        // 싱글톤 및 씬 전환 시 파괴 방지
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    // [방법 B용] 배경음 재생
    public void PlayBGM(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogError("[AudioDirector] 재생하려는 BGM Clip이 null입니다!");
            return;
        }

        if (bgmSource == null)
        {
            Debug.LogError("[AudioDirector] bgmSource(AudioSource)가 할당되지 않았습니다!");
            return;
        }

        if (bgmSource.clip == clip && bgmSource.isPlaying) return; 

        Debug.Log($"[AudioDirector] BGM 재생 명령: {clip.name} (Volume: {bgmSource.volume}, Mute: {bgmSource.mute})");
        bgmSource.clip = clip;
        bgmSource.loop = true;
        bgmSource.Play();
    }

    // [방법 B용] 환경음 재생 (동굴, 빗소리 등)
    public void PlayAmbient(AudioClip clip)
    {
        if (ambientSource.clip == clip) return;
        ambientSource.clip = clip;
        ambientSource.loop = true;
        ambientSource.Play();
    }

    // [방법 A & B 공용] 효과음 재생 (PlayOneShot은 여러 소리가 겹쳐도 잘 들림)
    public void PlaySFX(AudioClip clip, float pitch = 1.0f, float volume = 1.0f)
    {
        if (clip == null) return;

        // 피치 조절이 필요한 경우(매치 콤보 등)를 위해 설정
        sfxSource.pitch = pitch;
        sfxSource.volume = volume;
        sfxSource.PlayOneShot(clip);
    }
    public void PlayPeaceBGM()
    {
        // [v14.66] 동일 곡 중복 재생 방지 로직 포함
        PlayBGM(peaceBGM);
    }

    public void PlayCombatBGM()
    {
        // [v14.76] 0~배열길이 중 랜덤 선택
        int randomIndex = Random.Range(0, combatBGMs.Length);
        PlayBGM(combatBGMs[randomIndex]);
    }
    public void PlayBossBGM()
    {
        if (bossBGM != null)
        {
            PlayBGM(bossBGM); // [v14.66] 기존 곡을 즉시 교체하며 루프 재생
        }
    }
    public void StopBGM() => bgmSource.Stop();
    public void StopAmbient() => ambientSource.Stop();
    public void PlayClick() => PlaySFX(buttonClickSFX);
    public void PlayMatch(int combo) => PlaySFX(matchSFX, 1.0f + (combo * 0.1f));
    public void PlayBuy() => PlaySFX(itemBuySFX);
    public void PlayItemUse() => PlaySFX(itemUseSFX);
    public void PlayPlayerHit() => PlaySFX(playerHitSFX);
    public void PlayBombReload() => PlaySFX(bombReloadSFX, 1.0f);
    public void PlayBombExplosion() => PlaySFX(bombExplodeSFX, 1.2f); // 폭발은 피치를 높게
    public void PlayBoardStrike() => PlaySFX(strikeSFX, 0.9f, 0.0f);
    public void PlayBombThrow() => PlaySFX(bombThrowSFX, 1.0f);
    public void PlayDeath() => PlaySFX(DeathSFX, 0.8f, 0.6f);
    public void PlaySqueak()
    {
        // 호출은 단순하게, 내부 처리는 스마트하게!
        float randomPitch = UnityEngine.Random.Range(0.95f, 1.05f);
        PlaySFX(mouseSqueakSFX, randomPitch,0.5f);
    }
}