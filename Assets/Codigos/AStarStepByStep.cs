
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AStarStepByStep : MonoBehaviour
{
    private class Node
    {
        public int x, y;
        public int cost;
        public int radiationCost;
        public int g = int.MaxValue;
        public int h;
        public Node parent;
        public State state;

        public int f
        {
            get
            {
                return g == int.MaxValue ? int.MaxValue : g + h;
            }
        }
    }

    private enum State
    {
        None,
        Open,
        Closed,
        Path
    }

    [Header("Grade")]
    public float nodeSize = 1f;
    public Vector2Int size = new Vector2Int(20, 20);

    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float obstacleHeight = 1f;

    [Header("Pontos")]
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform targetPoint;

    [Header("Configuracoes")]
    [SerializeField] private bool automatic = true;
    [SerializeField] private bool showGridGizmos = true;

    [Header("Movimento")]
    [SerializeField] private float movementSpeed = 3f;

    [Header("Radiacao")]
    [SerializeField]
    private List<Transform> radiationSources =
        new List<Transform>();

    [SerializeField] private float radiationUpdateInterval = 0.5f;

    [Header("Posicao da Grade")]
    [SerializeField] private bool autoCenterGrid = true;
    [SerializeField] private Vector3 gridOrigin;

    private Node[,] nodes;
    private readonly List<Node> openNodes = new List<Node>();
    private readonly List<Node> finalPath = new List<Node>();

    private Node targetNode;
    private bool finished;
    private float nextRadiationUpdateTime;
    private Coroutine movementCoroutine;

    void Start()
    {
        CalculateGridOrigin();
        CreateGrid();

        UpdateRadiationCosts();
        ResetSearch();
    }

    void Update()
    {
        if (automatic && !finished)
        {
            StepSearch();
        }

        if (Time.time >= nextRadiationUpdateTime)
        {
            nextRadiationUpdateTime =
                Time.time + radiationUpdateInterval;

            if (UpdateRadiationCostsInternal())
            {
                StopMovement();
                ResetSearch();
            }
        }
    }

    // Define a origem da grade usando os pontos inicial e final.
    void CalculateGridOrigin()
    {
        if (!autoCenterGrid ||
            startPoint == null ||
            targetPoint == null)
        {
            return;
        }

        Vector3 center =
            (startPoint.position + targetPoint.position) / 2f;

        float width = (size.x - 1) * nodeSize;
        float height = (size.y - 1) * nodeSize;

        gridOrigin = new Vector3(
            center.x - width / 2f,
            0f,
            center.z - height / 2f
        );
    }

    void CreateGrid()
    {
        nodes = new Node[size.x, size.y];

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Node node = new Node();

                node.x = x;
                node.y = y;

                node.cost = IsWalkable(
                    NodeToWorld(x, y)
                ) ? 1 : int.MaxValue;

                node.radiationCost = 0;

                nodes[x, y] = node;
            }
        }
    }

    // Funcao publica exigida pelo exercicio.
    public void UpdateRadiationCosts()
    {
        UpdateRadiationCostsInternal();
    }

    bool UpdateRadiationCostsInternal()
    {
        if (nodes == null)
            return false;

        bool changed = false;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Node node = nodes[x, y];

                int oldRadiation = node.radiationCost;

                node.radiationCost = 0;

                // Obstaculos continuam impossiveis.
                if (node.cost == int.MaxValue)
                {
                    node.radiationCost = int.MaxValue;
                    continue;
                }

                foreach (Transform source in radiationSources)
                {
                    if (source == null)
                        continue;

                    Node sourceNode =
                        WorldToNode(source.position);

                    int distance =
                        Mathf.Abs(node.x - sourceNode.x) +
                        Mathf.Abs(node.y - sourceNode.y);

                    // Valores fixos de perigo exigidos.
                    switch (distance)
                    {
                        case 0:
                            node.radiationCost += 8;
                            break;

                        case 1:
                            node.radiationCost += 5;
                            break;

                        case 2:
                            node.radiationCost += 3;
                            break;

                        case 3:
                            node.radiationCost += 1;
                            break;
                    }
                }

                if (oldRadiation != node.radiationCost)
                {
                    changed = true;
                }
            }
        }

        return changed;
    }

    [ContextMenu("Reiniciar busca")]
    public void ResetSearch()
    {
        StopMovement();

        if (nodes == null)
            return;

        openNodes.Clear();
        finalPath.Clear();

        finished = false;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                nodes[x, y].g = int.MaxValue;
                nodes[x, y].h = 0;
                nodes[x, y].parent = null;
                nodes[x, y].state = State.None;
            }
        }

        Node start = WorldToNode(
            startPoint != null
                ? startPoint.position
                : transform.position
        );

        targetNode = WorldToNode(
            targetPoint != null
                ? targetPoint.position
                : transform.position
        );

        if (start.cost == int.MaxValue ||
            targetNode.cost == int.MaxValue)
        {
            Debug.LogWarning(
                "Inicio ou destino esta em um obstaculo.",
                this
            );

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
        if (finished)
            return;

        Node current = GetBestOpenNode();

        if (current == null)
        {
            finished = true;

            Debug.LogWarning(
                "Nao existe caminho.",
                this
            );

            return;
        }

        openNodes.Remove(current);
        current.state = State.Closed;

        if (current == targetNode)
        {
            finished = true;

            CreatePath();

            StartMovement();

            return;
        }

        foreach (Node neighbor in GetNeighbors(current))
        {
            if (neighbor.cost == int.MaxValue ||
                neighbor.state == State.Closed)
            {
                continue;
            }

            // A radiacao aumenta o custo G.
            // Assim, caminhos mais longos e seguros
            // podem ser escolhidos em vez de atalhos perigosos.
            int newG =
                current.g +
                neighbor.cost +
                neighbor.radiationCost;

            if (newG >= neighbor.g)
                continue;

            neighbor.g = newG;
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
            if (best == null ||
                node.f < best.f ||
                (node.f == best.f &&
                 node.h < best.h))
            {
                best = node;
            }
        }

        return best;
    }

    IEnumerable<Node> GetNeighbors(Node node)
    {
        if (node.x > 0)
            yield return nodes[node.x - 1, node.y];

        if (node.x + 1 < size.x)
            yield return nodes[node.x + 1, node.y];

        if (node.y > 0)
            yield return nodes[node.x, node.y - 1];

        if (node.y + 1 < size.y)
            yield return nodes[node.x, node.y + 1];
    }

    int Distance(Node a, Node b)
    {
        return Mathf.Abs(a.x - b.x) +
               Mathf.Abs(a.y - b.y);
    }

    void CreatePath()
    {
        finalPath.Clear();

        for (Node node = targetNode;
             node != null;
             node = node.parent)
        {
            node.state = State.Path;
            finalPath.Add(node);
        }

        finalPath.Reverse();
    }

    // Inicia o movimento do jogador pelo caminho.
    void StartMovement()
    {
        if (startPoint == null ||
            finalPath.Count == 0)
        {
            return;
        }

        StopMovement();

        movementCoroutine =
            StartCoroutine(MoveAlongPath());
    }

    IEnumerator MoveAlongPath()
    {
        foreach (Node node in finalPath)
        {
            Vector3 destination =
                NodeToWorld(node.x, node.y);

            destination.y = startPoint.position.y;

            while (Vector3.Distance(
                startPoint.position,
                destination
            ) > 0.05f)
            {
                startPoint.position = Vector3.MoveTowards(
                    startPoint.position,
                    destination,
                    movementSpeed * Time.deltaTime
                );

                yield return null;
            }
        }

        if (targetPoint != null)
        {
            Vector3 finalPosition = targetPoint.position;

            finalPosition.y = startPoint.position.y;

            while (Vector3.Distance(
                startPoint.position,
                finalPosition
            ) > 0.05f)
            {
                startPoint.position = Vector3.MoveTowards(
                    startPoint.position,
                    finalPosition,
                    movementSpeed * Time.deltaTime
                );

                yield return null;
            }
        }

        movementCoroutine = null;
    }

    void StopMovement()
    {
        if (movementCoroutine != null)
        {
            StopCoroutine(movementCoroutine);
            movementCoroutine = null;
        }
    }

    Node WorldToNode(Vector3 world)
    {
        int x = Mathf.RoundToInt(
            (world.x - gridOrigin.x) / nodeSize
        );

        int y = Mathf.RoundToInt(
            (world.z - gridOrigin.z) / nodeSize
        );

        x = Mathf.Clamp(x, 0, size.x - 1);
        y = Mathf.Clamp(y, 0, size.y - 1);

        return nodes[x, y];
    }

    Vector3 NodeToWorld(int x, int y)
    {
        return new Vector3(
            gridOrigin.x + x * nodeSize,
            0f,
            gridOrigin.z + y * nodeSize
        );
    }

    bool IsWalkable(Vector3 center)
    {
        Vector3 halfExtents = new Vector3(
            nodeSize * 0.45f,
            obstacleHeight * 0.5f,
            nodeSize * 0.45f
        );

        return !Physics.CheckBox(
            center,
            halfExtents,
            Quaternion.identity,
            obstacleMask
        );
    }

    void OnDrawGizmos()
    {
        if (!showGridGizmos ||
            size.x <= 0 ||
            size.y <= 0)
        {
            return;
        }

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Node node =
                    nodes == null
                        ? null
                        : nodes[x, y];

                bool walkable =
                    node == null
                        ? IsWalkable(NodeToWorld(x, y))
                        : node.cost != int.MaxValue;

                Gizmos.color = GetColor(node, walkable);

                Gizmos.DrawCube(
                    NodeToWorld(x, y),
                    new Vector3(
                        nodeSize * 0.9f,
                        0.05f,
                        nodeSize * 0.9f
                    )
                );
            }
        }

        Gizmos.color = Color.yellow;

        foreach (Transform source in radiationSources)
        {
            if (source != null)
            {
                Gizmos.DrawWireSphere(
                    source.position,
                    4f * nodeSize
                );
            }
        }
    }

    Color GetColor(Node node, bool walkable)
    {
        if (!walkable)
            return Color.black;

        if (node == null)
            return Color.green;

        // O caminho final tem prioridade visual.
        if (node.state == State.Path)
            return Color.cyan;

        int radiation = node.radiationCost;

        if (radiation == 0)
            return Color.green;

        if (radiation <= 4)
            return Color.yellow;

        if (radiation <= 8)
            return Color.red;

        return new Color(0.5f, 0f, 0f);
    }
}