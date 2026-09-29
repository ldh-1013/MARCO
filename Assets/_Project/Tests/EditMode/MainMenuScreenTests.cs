using System.Reflection;
using Marco.Presentation.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 메인 메뉴(09-30): "솔로 연습장 — 준비 중" 줄은 숨긴다(GAP-31 이월 — 코드는 남기고 비활성). 방 만들기 · 코드 입장 줄은 그대로.
    /// </summary>
    public class MainMenuScreenTests
    {
        [Test]
        public void SoloPracticeLine_IsHidden_HostJoinLineStays()
        {
            var go = new GameObject("MainMenu");
            try
            {
                var screen = go.AddComponent<MainMenuScreen>();
                typeof(MainMenuScreen).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(screen, null);

                Text hint = null, menu = null;
                foreach (Text t in go.GetComponentsInChildren<Text>(includeInactive: true))
                {
                    if (t.name == "MainMenu_Hint") hint = t;
                    if (t.name == "MainMenu_Menu") menu = t;
                }

                Assert.IsNotNull(hint, "줄 오브젝트는 남아 있다(코드 삭제 금지)");
                Assert.AreEqual("솔로 연습장 — 준비 중", hint.text, "문구 코드는 그대로");
                Assert.IsFalse(hint.gameObject.activeInHierarchy, "'솔로 연습장 — 준비 중' 줄은 보이지 않는다(수정 전: 보였다)");

                Assert.IsNotNull(menu);
                Assert.IsTrue(menu.gameObject.activeInHierarchy, "방 만들기 · 코드 입장 줄은 그대로 보인다");
                StringAssert.Contains("방 만들기", menu.text);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
