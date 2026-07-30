using UnityEngine;
using UnityEngine.UI;
public class UIButtonAudio : MonoBehaviour
{
    public Button myButton; // 이제 여기서 onClick을 사용할 수 있습니다.

    void Start()
    {
        // 소문자 o가 아니라 대문자 C인 'onClick'입니다.
        myButton.onClick.AddListener(() => {
            Debug.Log("버튼 클릭됨! 소리 재생 시도 중..."); // 이 메시지가 콘솔창에 뜨는지 확인
            AudioDirector.Instance.PlayClick();
        });
    }
}
