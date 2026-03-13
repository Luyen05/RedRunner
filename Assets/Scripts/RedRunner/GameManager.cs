using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

using BayatGames.SaveGameFree;
using BayatGames.SaveGameFree.Serializers;

using RedRunner.Characters;
using RedRunner.Collectables;
using RedRunner.TerrainGeneration;

namespace RedRunner
{
    /// Quản lý trạng thái trò chơi, điểm số, tiền tệ và các sự kiện quan trọng.
    /// Sử dụng Singleton Pattern để đảm bảo chỉ có duy nhất một instance trong trò chơi.
    public sealed class GameManager : MonoBehaviour
    {
        /// Delegate handler cho sự kiện âm thanh bật/tắt
        public delegate void AudioEnabledHandler(bool active);

        /// Delegate handler cho sự kiện thay đổi điểm số
        public delegate void ScoreHandler(float newScore, float highScore, float lastScore);

        /// Delegate handler cho sự kiện reset trò chơi
        public delegate void ResetHandler();

        /// Sự kiện trigger khi reset trò chơi
        public static event ResetHandler OnReset;
        /// Sự kiện trigger khi điểm số thay đổi
        public static event ScoreHandler OnScoreChanged;
        /// Sự kiện trigger khi bật/tắt âm thanh
        public static event AudioEnabledHandler OnAudioEnabled;

        /// Instance duy nhất của GameManager (Singleton Pattern)
        private static GameManager m_Singleton;

        /// Truy cập instance duy nhất của GameManager
        public static GameManager Singleton
        {
            get
            {
                return m_Singleton;
            }
        }

        /// Nhân vật chính của trò chơi
        [SerializeField]
        private Character m_MainCharacter;
        /// Text dùng để chia sẻ trên mạng xã hội
        [SerializeField]
        [TextArea(3, 30)]
        private string m_ShareText;
        /// URL dùng để chia sẻ trên mạng xã hội
        [SerializeField]
        private string m_ShareUrl;
        /// Vị trí X ban đầu khi bắt đầu tính điểm
        private float m_StartScoreX = 0f;
        /// Điểm số cao nhất từng đạt được
        private float m_HighScore = 0f;
        /// Điểm số lần chơi cuối cùng
        private float m_LastScore = 0f;
        /// Điểm số hiện tại (dựa trên vị trí X của nhân vật)
        private float m_Score = 0f;

        /// Trạng thái trò chơi đã bắt đầu hay chưa
        private bool m_GameStarted = false;
        /// Trạng thái trò chơi đang chạy hay tạm dừng
        private bool m_GameRunning = false;
        /// Trạng thái âm thanh bật/tắt
        private bool m_AudioEnabled = true;

        /// This is my developed callbacks compoents, because callbacks are so dangerous to use we need something that automate the sub/unsub to functions
        /// with this in-house developed callbacks feature, we garantee that the callback will be removed when we don't need it.
        /// Số lượng đồng xu đã thu thập (sử dụng Property với callback tự động)
        public Property<int> m_Coin = new Property<int>(0);


        #region Getters
        /// Kiểm tra trò chơi đã bắt đầu hay chưa
        public bool gameStarted
        {
            get
            {
                return m_GameStarted;
            }
        }

        /// Kiểm tra trò chơi đang chạy hay tạm dừng
        public bool gameRunning
        {
            get
            {
                return m_GameRunning;
            }
        }

        /// Kiểm tra âm thanh bật hay tắt
        public bool audioEnabled
        {
            get
            {
                return m_AudioEnabled;
            }
        }
        #endregion

        /// Khởi tạo Singleton. Nếu đã tồn tại instance khác, hủy game object này.
        /// Tải dữ liệu đã lưu từ file (coin, audioEnabled, lastScore, highScore)
        void Awake()
        {
            if (m_Singleton != null)
            {
                Destroy(gameObject);
                return;
            }
            SaveGame.Serializer = new SaveGameBinarySerializer();
            m_Singleton = this;
            m_Score = 0f;

            if (SaveGame.Exists("coin"))
            {
                m_Coin.Value = SaveGame.Load<int>("coin");
            }
            else
            {
                m_Coin.Value = 0;
            }
            if (SaveGame.Exists("audioEnabled"))
            {
                SetAudioEnabled(SaveGame.Load<bool>("audioEnabled"));
            }
            else
            {
                SetAudioEnabled(true);
            }
            if (SaveGame.Exists("lastScore"))
            {
                m_LastScore = SaveGame.Load<float>("lastScore");
            }
            else
            {
                m_LastScore = 0f;
            }
            if (SaveGame.Exists("highScore"))
            {
                m_HighScore = SaveGame.Load<float>("highScore");
            }
            else
            {
                m_HighScore = 0f;
            }

        }

        /// Xử lý sự kiện khi nhân vật chết
        void UpdateDeathEvent(bool isDead)
        {
            if (isDead)
            {
                StartCoroutine(DeathCrt());
            }
            else
            {
                StopCoroutine("DeathCrt");
            }
        }

        /// Coroutine xử lý logic sau khi nhân vật chết (1.5 giây, sau đó hiện màn hình kết thúc)
        IEnumerator DeathCrt()
        {
            m_LastScore = m_Score;
            if (m_Score > m_HighScore)
            {
                m_HighScore = m_Score;
            }
            if (OnScoreChanged != null)
            {
                OnScoreChanged(m_Score, m_HighScore, m_LastScore);
            }

            yield return new WaitForSecondsRealtime(1.5f);

            EndGame();
            var endScreen = UIManager.Singleton.UISCREENS.Find(el => el.ScreenInfo == UIScreenInfo.END_SCREEN);
            UIManager.Singleton.OpenScreen(endScreen);
        }

        /// Khởi tạo khi scene tải lên: đăng ký sự kiện chết, lưu vị trí bắt đầu, gọi Init()
        private void Start()
        {
            m_MainCharacter.IsDead.AddEventAndFire(UpdateDeathEvent, this);
            m_StartScoreX = m_MainCharacter.transform.position.x;
            Init();
        }

        /// Reset trò chơi, khởi tạo UIManager, tải màn hình khởi động
        public void Init()
        {
            EndGame();
            UIManager.Singleton.Init();
            StartCoroutine(Load());
        }

        /// Cập nhật điểm số lên dựa trên vị trí X của nhân vật (mỗi frame)
        void Update()
        {
            if (m_GameRunning)
            {
                if (m_MainCharacter.transform.position.x > m_StartScoreX && m_MainCharacter.transform.position.x > m_Score)
                {
                    m_Score = m_MainCharacter.transform.position.x;
                    if (OnScoreChanged != null)
                    {
                        OnScoreChanged(m_Score, m_HighScore, m_LastScore);
                    }
                }
            }
        }

        /// Tải màn hình khởi động sau 3 giây
        IEnumerator Load()
        {
            var startScreen = UIManager.Singleton.UISCREENS.Find(el => el.ScreenInfo == UIScreenInfo.START_SCREEN);
            yield return new WaitForSecondsRealtime(3f);
            UIManager.Singleton.OpenScreen(startScreen);
        }

        /// Lưu dữ liệu game khi thoát ứng dụng
        void OnApplicationQuit()
        {
            if (m_Score > m_HighScore)
            {
                m_HighScore = m_Score;
            }
            SaveGame.Save<int>("coin", m_Coin.Value);
            SaveGame.Save<float>("lastScore", m_Score);
            SaveGame.Save<float>("highScore", m_HighScore);
        }

        /// Thoát ứng dụng
        public void ExitGame()
        {
            Application.Quit();
        }

        /// Bật/tắt âm thanh (toggle)
        public void ToggleAudioEnabled()
        {
            SetAudioEnabled(!m_AudioEnabled);
        }

        /// Đặt trạng thái âm thanh và trigger sự kiện OnAudioEnabled
        public void SetAudioEnabled(bool active)
        {
            m_AudioEnabled = active;
            AudioListener.volume = active ? 1f : 0f;
            if (OnAudioEnabled != null)
            {
                OnAudioEnabled(active);
            }
        }

        /// Bắt đầu trò chơi (bắt đầu và tiếp tục)
        public void StartGame()
        {
            m_GameStarted = true;
            ResumeGame();
        }

        /// Tạm dừng trò chơi (dừng chạy, Time.timeScale = 0)
        public void StopGame()
        {
            m_GameRunning = false;
            Time.timeScale = 0f;
        }

        /// Tiếp tục trò chơi (đang chạy, Time.timeScale = 1)
        public void ResumeGame()
        {
            m_GameRunning = true;
            Time.timeScale = 1f;
        }

        /// Kết thúc trò chơi (dừng và đánh dấu chưa bắt đầu)
        public void EndGame()
        {
            m_GameStarted = false;
            StopGame();
        }

        /// Spawn lại nhân vật chính
        public void RespawnMainCharacter()
        {
            RespawnCharacter(m_MainCharacter);
        }

        /// Spawn lại nhân vật tại vị trí block nhân vật của terrain
        public void RespawnCharacter(Character character)
        {
            Block block = TerrainGenerator.Singleton.GetCharacterBlock();
            if (block != null)
            {
                Vector3 position = block.transform.position;
                position.y += 2.56f;
                position.x += 1.28f;
                character.transform.position = position;
                character.Reset();
            }
        }

        /// Reset điểm số về 0 và trigger sự kiện OnReset
        public void Reset()
        {
            m_Score = 0f;
            if (OnReset != null)
            {
                OnReset();
            }
        }

        /// Chia sẻ trên Twitter
        public void ShareOnTwitter()
        {
            Share("https://twitter.com/intent/tweet?text={0}&url={1}");
        }

        /// Chia sẻ trên Google Plus
        public void ShareOnGooglePlus()
        {
            Share("https://plus.google.com/share?text={0}&href={1}");
        }

        /// Chia sẻ trên Facebook
        public void ShareOnFacebook()
        {
            Share("https://www.facebook.com/sharer/sharer.php?u={1}");
        }

        /// Mở URL chia sẻ với text tùy chỉnh
        public void Share(string url)
        {
            Application.OpenURL(string.Format(url, m_ShareText, m_ShareUrl));
        }

        /// Custom UnityEvent class cho sự kiện tải
        [System.Serializable]
        public class LoadEvent : UnityEvent
        {

        }

    }

}