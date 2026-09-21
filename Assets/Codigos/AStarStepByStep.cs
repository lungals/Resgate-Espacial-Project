using System.Collections.Generic;
using UnityEngine;

// A* usa G (custo real) + H (estimativa ao destino) para priorizar os nos.
// Radiacao: fontes moveis aumentam o custo das celulas proximas, entao o
// caminho com menos passos nem sempre e o mais seguro. O custo entra no G.
public class AStarStepByStep : MonoBehaviour
{
    private class Node
    {
        public int x, y;
        public int cost;          // 1 = caminhavel; int.MaxValue = parede
        public int radiation;     // custo extra por radiacao (dinamico)
        public int g = int.MaxValue;
        public int h;
        public Node parent;
        public State state;
        public int TotalCost { get { return cost == int.MaxValue ? int.MaxValue : cost + radiation; } }
        public int f { get { return g == int.MaxValue ? int.MaxValue : g + h; } }
    }

    // Uma fonte de radiacao movel: arraste objetos da cena para a lista.
    [System.Serializable]
    private class RadiationSource
    {
        public Transform point;
        public float radius = 4f;   // alcance em unidades do mundo
        public int maxPenalty = 8;  // custo extra no centro da fonte
    }

    private enum State { None, Open, Closed, Path }

    public float nodeSize = 1f;
    public Vector2Int size = new Vector2Int(10, 10);
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float obstacleHeight = 1f;
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform targetPoint;
    [SerializeField] private bool automatic;
    [SerializeField] private bool showGridGizmos = true;
    [Header("Radiacao")]
    [SerializeField] private List<RadiationSource> radiationSources = new List<RadiationSource>();
    [SerializeField] private float radiationUpdateInterval = 0.5f;

    private Node[,] nodes;
    private readonly List<Node> openNodes = new List<Node>();
    private Node targetNode;
    private bool finished;
    private float radiationTimer;

    void Start()
    {
        CreateGrid();
        UpdateRadiationCosts();
        ResetSearch();
    }

    void Update()
    {
        // Espaco mostra uma expansao do algoritmo por vez.
        if (Input.GetKey(KeyCode.Space)) StepSearch();
        if (Input.GetKeyDown(KeyCode.R)) ResetSearch();
        if (automatic && !finished) StepSearch();

        // A cada 0,5s: se alguma fonte de radiacao se moveu, recalcula os
        // custos e reinicia a busca, refazendo a rota com os novos riscos.
        radiationTimer += Time.deltaTime;
        if (radiationTimer >= radiationUpdateInterval)
        {
            radiationTimer = 0f;
            if (UpdateRadiationCosts()) ResetSearch();
        }
    }

    void CreateGrid()
    {
        nodes = new Node[size.x, size.y];
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        {
            Node node = new Node();
            node.x = x;
            node.y = y;
            node.cost = IsWalkable(NodeToWorld(x, y)) ? 1 : int.MaxValue;
            nodes[x, y] = node;
        }
    }

    // Recalcula o custo extra de radiacao de cada celula com base na
    // posicao atual das fontes. Retorna true se algum custo mudou.
    bool UpdateRadiationCosts()
    {
        if (nodes == null) return false;
        bool changed = false;
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        {
            Node node = nodes[x, y];
            int penalty = 0;
            if (node.cost != int.MaxValue)
            {
                Vector3 center = NodeToWorld(x, y);
                foreach (RadiationSource source in radiationSources)
                {
                    if (source.point == null) continue;
                    float dist = Vector3.Distance(center, source.point.position);
                    if (dist >= source.radius) continue;
                    // Quanto mais perto do centro, maior o custo extra.
                    penalty += Mathf.RoundToInt(source.maxPenalty * (1f - dist / source.radius));
                }
            }
            if (node.radiation != penalty)
            {
                node.radiation = penalty;
                changed = true;
            }
        }
        return changed;
    }

    [ContextMenu("Reiniciar busca")]
    public void ResetSearch()
    {
        if (nodes == null || size.x <= 0 || size.y <= 0) return;
        openNodes.Clear();
        finished = false;

        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        {
            nodes[x, y].g = int.MaxValue;
            nodes[x, y].h = 0;
            nodes[x, y].parent = null;
            nodes[x, y].state = State.None;
        }

        Node start = WorldToNode(startPoint != null ? startPoint.position : transform.position);
        targetNode = WorldToNode(targetPoint != null ? targetPoint.position : transform.position);
        if (start.cost == int.MaxValue || targetNode.cost == int.MaxValue)
        {
            finished = true;
            return;
        }

        start.g = 0;
        start.h = Distance(start, targetNode);
        start.state = State.Open;
        openNodes.Add(start);
    }

    [ContextMenu("Proximo passo")]
    public void StepSearch()
    {
        if (finished) return;

        // Regra central do A*: menor F, onde F = G + H.
        Node current = GetBestOpenNode();
        if (current == null)
        {
            finished = true;
            Debug.Log("Nao existe caminho.", this);
            return;
        }

        openNodes.Remove(current);
        current.state = State.Closed;
        if (current == targetNode)
        {
            finished = true;
            CreatePath();
            return;
        }

        foreach (Node neighbor in GetNeighbors(current))
        {
            if (neighbor.cost == int.MaxValue || neighbor.state == State.Closed) continue;
            // G agora soma custo base + radiacao: rotas mais longas porem
            // seguras ganham de atalhos radioativos quando o custo e maior.
            int newG = current.g + neighbor.TotalCost;
            if (newG >= neighbor.g) continue;

            neighbor.g = newG;
            // H e a distancia Manhattan: adequada porque nao ha diagonais.
            neighbor.h = Distance(neighbor, targetNode);
            neighbor.parent = current;
            if (neighbor.state != State.Open)
            {
                neighbor.state = State.Open;
                openNodes.Add(neighbor);
            }
        }
    }

    Node GetBestOpenNode()
    {
        Node best = null;
        foreach (Node node in openNodes)
        {
            // H desempata e deixa a direcao ao alvo mais visivel.
            if (best == null || node.f < best.f || (node.f == best.f && node.h < best.h)) best = node;
        }
        return best;
    }

    IEnumerable<Node> GetNeighbors(Node node)
    {
        if (node.x > 0) yield return nodes[node.x - 1, node.y];
        if (node.x + 1 < size.x) yield return nodes[node.x + 1, node.y];
        if (node.y > 0) yield return nodes[node.x, node.y - 1];
        if (node.y + 1 < size.y) yield return nodes[node.x, node.y + 1];
    }

    int Distance(Node a, Node b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    void CreatePath()
    {
        for (Node node = targetNode; node != null; node = node.parent) node.state = State.Path;
    }

    Node WorldToNode(Vector3 world)
    {
        // Mantem a grade fixa na origem do mundo, como no AIMove.
        int x = Mathf.Clamp(Mathf.FloorToInt(world.x / nodeSize), 0, size.x - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(world.z / nodeSize), 0, size.y - 1);
        return nodes[x, y];
    }

    Vector3 NodeToWorld(int x, int y)
    {
        return new Vector3(x * nodeSize, 0, y * nodeSize);
    }

    bool IsWalkable(Vector3 center)
    {
        Vector3 halfExtents = new Vector3(nodeSize * .45f, obstacleHeight * .5f, nodeSize * .45f);
        return !Physics.CheckBox(center, halfExtents, Quaternion.identity, obstacleMask);
    }

    void OnDrawGizmos()
    {
        if (!showGridGizmos || size.x <= 0 || size.y <= 0) return;
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        {
            Node node = nodes == null ? null : nodes[x, y];
            bool walkable = node == null ? IsWalkable(NodeToWorld(x, y)) : node.cost != int.MaxValue;
            Gizmos.color = GetColor(node, walkable);
            Gizmos.DrawCube(NodeToWorld(x, y), new Vector3(nodeSize * .9f, .05f, nodeSize * .9f));
        }

        // Contorno amarelo ao redor de cada fonte de radiacao.
        Gizmos.color = new Color(1f, .9f, 0f, .8f);
        foreach (RadiationSource source in radiationSources)
        {
            if (source.point == null) continue;
            Gizmos.DrawWireSphere(source.point.position, source.radius);
        }
    }

    Color GetColor(Node node, bool walkable)
    {
        if (!walkable) return new Color(1, 0, 0, .35f);

        // Celulas com radiacao ficam em tons de vermelho/laranja conforme o
        // risco, por cima do verde padrao. O caminho final continua ciano.
        if (node != null && node.radiation > 0 && node.state != State.Path)
        {
            float risk = Mathf.Clamp01(node.radiation / 10f);
            return Color.Lerp(new Color(1f, .8f, 0f, .45f), new Color(1f, 0f, 0f, .6f), risk);
        }

        if (node == null || node.state == State.None) return new Color(0, 1, 0, .25f);
        if (node.state == State.Open) return new Color(1, .65f, 0, .55f);
        if (node.state == State.Closed) return new Color(.2f, .5f, 1, .55f);
        return Color.cyan;
    }
}
