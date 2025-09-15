using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using DG.Tweening;          // DOTween
using DD.Web3;              // your blockchain namespace

public class CollectionManager : MonoBehaviour
{
    [Header("Mint Buttons → same order as Sprites")]
    public List<Button> Buttons;
    public List<Sprite> Sprites;

    [Header("World Trigger Points (same order as Buttons/Sprites)")]
    public List<GameObject> TriggerPoints;

    [Header("Prefab & Parent")]
    public GameObject nftCardPrefab;    // Prefab must contain an Image (root or child)
    public Transform gridParent;        // Grid/Content parent

    [Header("Optional: Resources fallback folder (e.g., \"NFTs\")")]
    public string resourcesFolder = ""; // Leave empty if not using Resources

    [Header("Blockchain UI (optional)")]
    public GameObject panel;            // Loading/transaction panel (optional)
    public List<Button> claimButtons;   // Buttons to disable/hide after success (optional)

    [Header("Blockchain Settings")]
    [Tooltip("ERC20 claim amount used during mint flow")]
    public int claimAmount = 1;         // default 1 (as requested)

    [Header("Trigger Settings")]
    [Tooltip("When true, only the current zone’s button is visible")]
    public bool exclusiveActivation = true;
    [Tooltip("Tag used to detect the player in trigger zones")]
    public string playerTag = "Player";

    [Header("Tween Settings")]
    public bool useTween = true;
    public float showDuration = 0.35f;
    public float hideDuration = 0.20f;
    public Ease showEase = Ease.OutBack;
    public Ease hideEase = Ease.InBack;
    [Tooltip("Hide complete par GameObject ko deactivate kare?")]
    public bool deactivateOnHide = true;

    private int currentIndex = 0;

    // PlayerPrefs keys
    private const string SaveCountKey = "NFTCount";
    private const string SaveKeyPrefix = "NFT_";

    // Fast lookup for assigned sprites
    private Dictionary<string, Sprite> spriteMap;

    void Awake()
    {
        BuildSpriteMap();
    }

    void Start()
    {
        // Wire button → StartMint(index)
        for (int i = 0; i < Buttons.Count && i < Sprites.Count; i++)
        {
            int index = i; // local for lambda
            if (!Buttons[i]) continue;
            Buttons[i].onClick.RemoveAllListeners();
            Buttons[i].onClick.AddListener(() => StartMint(index));
        }

        // Initially hide all mint buttons (scale 0)
        HideAllButtons();

        // Auto-setup trigger zones
        SetupTriggers();

        // Rebuild saved cards
        LoadNFTs();
    }

    // ---------- Public Mint Entry (per button index) ----------
    private void StartMint(int spriteIndex)
    {
        if (spriteIndex < 0 || spriteIndex >= Sprites.Count)
        {
            Debug.LogWarning($"StartMint: Invalid sprite index {spriteIndex}");
            return;
        }

        // If blockchain manager exists, go through on-chain claim flow.
        if (BlockchainManager.Instance != null && BlockchainManager.Instance.connectionManager != null)
        {
            ShowLoading(true);
            HandleClaimFlow(spriteIndex);
        }
        else
        {
            Debug.Log("Blockchain or Wallet is not being used! Falling back to local mint.");
            // Fallback: local add (useful in editor/testing)
            AddNFT(Sprites[spriteIndex]);
            // Optionally hide/disable the button after local mint
            SetButtonActive(spriteIndex, false);
        }
    }

    // ---------- Blockchain Flow ----------
    private void HandleClaimFlow(int spriteIndex)
    {
        var cm = BlockchainManager.Instance?.connectionManager;
        if (cm == null)
        {
            Debug.LogWarning("HandleClaimFlow: connectionManager is null. Falling back to local mint.");
            AddNFT(Sprites[spriteIndex]);
            SetButtonActive(spriteIndex, false);
            ShowLoading(false);
            return;
        }

        // Claim ERC20 (or whatever your flow triggers) and only add NFT on success
        cm.ClaimDropERC20(claimAmount, (result) =>
        {
            if (result)
                OnTransactionSuccessful(spriteIndex);
            else
                OnTransactionFailed();
        });
    }

    private void OnTransactionSuccessful(int spriteIndex)
    {
        Debug.Log("Transaction successful.");

        ShowLoading(false);

        if (panel) panel.SetActive(false);
        if (claimButtons != null)
        {
            foreach (var btn in claimButtons)
                if (btn) btn.gameObject.SetActive(false);
        }

        if (spriteIndex >= 0 && spriteIndex < Sprites.Count)
        {
            // Only now do we mint locally -> creates card + saves
            AddNFT(Sprites[spriteIndex]);

            // Smoothly hide the clicked mint button
            SetButtonActive(spriteIndex, false);
        }
        else
        {
            Debug.LogWarning($"OnTransactionSuccessful: Invalid sprite index {spriteIndex}");
        }
    }

    private void OnTransactionFailed()
    {
        Debug.Log("Transaction failed.");
        if (panel) panel.SetActive(false);
        ShowLoading(false);
    }

    private void ShowLoading(bool show)
    {
        var cm = BlockchainManager.Instance?.connectionManager;
        if (cm != null)
            cm.ShowLoadingScreen(show);
    }

    // ---------- Trigger Wiring ----------
    private void SetupTriggers()
    {
        int count = Mathf.Min(
            TriggerPoints != null ? TriggerPoints.Count : 0,
            Buttons != null ? Buttons.Count : 0,
            Sprites != null ? Sprites.Count : 0
        );

        for (int i = 0; i < count; i++)
        {
            var go = TriggerPoints[i];
            if (!go) continue;

            // Ensure a collider exists and is trigger
            var col = go.GetComponent<Collider>();
            if (!col)
            {
                Debug.LogWarning($"Trigger point '{go.name}' had no Collider. Adding BoxCollider (isTrigger=true).");
                col = go.AddComponent<BoxCollider>();
            }
            col.isTrigger = true;

            // Attach / configure trigger script
            var zone = go.GetComponent<TriggerZoneMint>();
            if (!zone) zone = go.AddComponent<TriggerZoneMint>();
            zone.manager = this;
            zone.index = i;
            zone.playerTag = playerTag;
        }
    }

    public void OnPlayerEnterZone(int index)
    {
        if (exclusiveActivation) HideAllButtons();
        SetButtonActive(index, true);
    }

    public void OnPlayerExitZone(int index)
    {
        SetButtonActive(index, false);
    }

    // ---------- Tweened Show/Hide ----------
    private void HideAllButtons()
    {
        if (Buttons == null) return;
        foreach (var b in Buttons)
        {
            if (!b) continue;
            var t = b.transform;
            t.DOKill();
            t.localScale = Vector3.zero;

            if (deactivateOnHide)
                b.gameObject.SetActive(false);
            else
                b.gameObject.SetActive(true); // keep active if you want layout to reserve space

            b.interactable = false;

            var cg = b.GetComponent<CanvasGroup>();
            if (cg) cg.blocksRaycasts = false;
        }
    }

    private void SetButtonActive(int index, bool active)
    {
        if (index < 0 || index >= Buttons.Count) return;
        var b = Buttons[index];
        if (!b) return;

        if (!useTween)
        {
            b.gameObject.SetActive(active);
            b.interactable = active;
            b.transform.localScale = active ? Vector3.one : Vector3.zero;
            var cg0 = b.GetComponent<CanvasGroup>();
            if (cg0) cg0.blocksRaycasts = active;
            return;
        }

        if (active) ShowButton(b);
        else HideButton(b);
    }

    private void ShowButton(Button b)
    {
        if (!b) return;
        var t = b.transform;

        // Activate first so DOTween can animate
        b.gameObject.SetActive(true);
        b.interactable = false; // disable clicks during animation

        var cg = b.GetComponent<CanvasGroup>();
        if (cg) { cg.blocksRaycasts = false; cg.alpha = 1f; }

        t.DOKill();

        // Ensure starting scale = 0 for clean pop
        if (t.localScale.x > 0.001f) t.localScale = Vector3.zero;

        t.DOScale(1f, showDuration).SetEase(showEase).OnComplete(() =>
        {
            b.interactable = true;
            if (cg) cg.blocksRaycasts = true;
        });
    }

    private void HideButton(Button b)
    {
        if (!b) return;
        var t = b.transform;

        b.interactable = false;

        var cg = b.GetComponent<CanvasGroup>();
        if (cg) cg.blocksRaycasts = false;

        t.DOKill();
        t.DOScale(0f, hideDuration).SetEase(hideEase).OnComplete(() =>
        {
            if (deactivateOnHide) b.gameObject.SetActive(false);
        });
    }

    // ---------- Storage / UI ----------
    private void BuildSpriteMap()
    {
        spriteMap = new Dictionary<string, Sprite>();
        foreach (var s in Sprites)
        {
            if (s != null && !spriteMap.ContainsKey(s.name))
                spriteMap.Add(s.name, s);
        }
    }

    // Mint/Add new NFT to collection (also saves)
    public void AddNFT(Sprite nftSprite)
    {
        if (nftSprite == null)
        {
            Debug.LogWarning("AddNFT: Sprite is null.");
            return;
        }

        if (!CreateCard(nftSprite))
        {
            Debug.LogError("AddNFT: Failed to create card. Check prefab/parent.");
            return;
        }

        // Save sprite name
        PlayerPrefs.SetString(SaveKeyPrefix + currentIndex, nftSprite.name);
        currentIndex++;
        PlayerPrefs.SetInt(SaveCountKey, currentIndex);
        PlayerPrefs.Save();

        Debug.Log($"NFT Minted & Saved: {nftSprite.name} (index {currentIndex - 1})");
    }

    // Load saved NFTs
    private void LoadNFTs()
    {
        // Optional: clear existing UI children so we don't duplicate on domain reload
        if (gridParent != null)
        {
            for (int i = gridParent.childCount - 1; i >= 0; i--)
                Destroy(gridParent.GetChild(i).gameObject);
        }

        int savedCount = PlayerPrefs.GetInt(SaveCountKey, 0);
        Debug.Log("Loading NFTs... Found saved count = " + savedCount);

        currentIndex = 0;

        for (int i = 0; i < savedCount; i++)
        {
            string spriteName = PlayerPrefs.GetString(SaveKeyPrefix + i, "");
            Debug.Log($"Slot {i} → Saved Sprite Name: {spriteName}");

            if (string.IsNullOrEmpty(spriteName))
            {
                Debug.LogWarning($"⚠ Slot {i}: No sprite name found in PlayerPrefs");
                continue;
            }

            // Try from assigned list first
            Sprite loadedSprite = spriteMap != null && spriteMap.TryGetValue(spriteName, out var s) ? s : null;

            // Fallback: Resources
            if (loadedSprite == null)
            {
                string path = string.IsNullOrEmpty(resourcesFolder) ? spriteName : $"{resourcesFolder}/{spriteName}";
                Debug.Log($"Slot {i}: Not found in Sprites list, checking Resources at '{path}'...");
                loadedSprite = Resources.Load<Sprite>(path);
            }

            if (loadedSprite != null)
            {
                if (CreateCard(loadedSprite))
                {
                    currentIndex++;
                    Debug.Log($"✅ Slot {i} loaded successfully: {loadedSprite.name}");
                }
                else
                {
                    Debug.LogError($"❌ Slot {i}: Card creation failed for {loadedSprite.name}");
                }
            }
            else
            {
                Debug.LogWarning($"❌ Slot {i} failed to load sprite: {spriteName}");
            }
        }

        PlayerPrefs.SetInt(SaveCountKey, currentIndex);
        Debug.Log("Load complete. CurrentIndex set to " + currentIndex);
    }

    // Instantiates prefab and assigns sprite
    private bool CreateCard(Sprite s)
    {
        if (nftCardPrefab == null || gridParent == null)
        {
            Debug.LogError("CreateCard: Prefab or Grid Parent is missing.");
            return false;
        }

        var go = Instantiate(nftCardPrefab, gridParent);
        var img = go.GetComponentInChildren<Image>(true);
        if (img == null)
        {
            Debug.LogError("CreateCard: NFT prefab must have an Image (on root or child).");
            Destroy(go);
            return false;
        }

        img.sprite = s;
        img.enabled = true;
        return true;
    }

    // Quick utility from Inspector to wipe saved data
    [ContextMenu("Clear Saved NFTs")]
    public void ClearSaved()
    {
        int count = PlayerPrefs.GetInt(SaveCountKey, 0);
        for (int i = 0; i < count; i++)
            PlayerPrefs.DeleteKey(SaveKeyPrefix + i);

        PlayerPrefs.DeleteKey(SaveCountKey);
        PlayerPrefs.Save();
        Debug.Log("Cleared saved NFTs.");
    }
}
