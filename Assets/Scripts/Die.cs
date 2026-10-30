using UnityEngine;
using UnityEngine.SceneManagement;

public class Die : MonoBehaviour
{
    public ParticleSystem die;
    private void OnCollisionEnter2D(Collision2D collision)
    {
        Debug.Log("fk");
        collision.gameObject.SetActive(false);
        Instantiate(die,collision.transform.position,Quaternion.identity);
        Invoke(nameof(restart),0.5f);
    }
    void restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
